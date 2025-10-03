using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SharpDX.DirectInput;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;

namespace RCCarController
{
    public partial class MainWindow : Window
    {
        // DirectX Input (Windows only)
        private DirectInput? directInput;
        private Joystick? joystick;
        private IList<DeviceInstance>? gameControllers;

        // Serial Communication
        private SerialPort? serialPort;
        private bool isConnected = false;

        // Control Values
        private int steeringValue = 90;  // 0-180
        private int throttleValue = 90;  // 1-180 (neutral)

        // Transmit Timer
        private System.Timers.Timer? transmitTimer;

        // Settings
        private const string SETTINGS_FILE = "settings.ini";
        private string? savedPort;

        public MainWindow()
        {
            InitializeComponent();
            if (OperatingSystem.IsWindows())
            {
                InitializeDirectInput();
            }
            LoadSettings();
            SetupTransmitTimer();
            // RefreshPorts(); // Moved to OnLoaded
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // Wire up events after XAML is loaded
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            // Get control references
            var steeringSlider = this.FindControl<Slider>("SteeringSlider");
            var steeringNumeric = this.FindControl<NumericUpDown>("SteeringNumeric");
            var throttleSlider = this.FindControl<Slider>("ThrottleSlider");
            var throttleNumeric = this.FindControl<NumericUpDown>("ThrottleNumeric");
            var refreshButton = this.FindControl<Button>("RefreshButton");
            var connectButton = this.FindControl<Button>("ConnectButton");

            // Wire up events
            if (steeringSlider != null) steeringSlider.ValueChanged += SteeringSlider_ValueChanged;
            if (steeringNumeric != null) steeringNumeric.ValueChanged += SteeringNumeric_ValueChanged;
            if (throttleSlider != null) throttleSlider.ValueChanged += ThrottleSlider_ValueChanged;
            if (throttleNumeric != null) throttleNumeric.ValueChanged += ThrottleNumeric_ValueChanged;
            if (refreshButton != null) refreshButton.Click += RefreshButton_Click;
            if (connectButton != null) connectButton.Click += ConnectButton_Click;

            // Key handling
            this.KeyDown += OnKeyDown;

            // Now safe to refresh ports
            RefreshPorts();
        }

        private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.A:
                case Avalonia.Input.Key.Left:
                    steeringValue = Math.Max(0, steeringValue - 5);
                    UpdateSteeringUI();
                    break;
                case Avalonia.Input.Key.D:
                case Avalonia.Input.Key.Right:
                    steeringValue = Math.Min(180, steeringValue + 5);
                    UpdateSteeringUI();
                    break;
                case Avalonia.Input.Key.W:
                case Avalonia.Input.Key.Up:
                    throttleValue = Math.Min(140, throttleValue + 1);
                    UpdateThrottleUI();
                    break;
                case Avalonia.Input.Key.S:
                case Avalonia.Input.Key.Down:
                    throttleValue = Math.Max(40, throttleValue - 1);
                    UpdateThrottleUI();
                    break;
            }
        }

        private void InitializeDirectInput()
        {
            try
            {
                directInput = new DirectInput();
                gameControllers = directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices);
                if (gameControllers.Count == 0)
                {
                    gameControllers = directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices);
                }

                if (gameControllers.Count > 0)
                {
                    joystick = new Joystick(directInput, gameControllers[0].InstanceGuid);
                    joystick.Properties.BufferSize = 128;
                    joystick.Acquire();
                    LogMessage("Game controller detected: " + gameControllers[0].ProductName);
                }
                else
                {
                    LogMessage("No game controllers detected");
                }
            }
            catch (Exception ex)
            {
                LogMessage("DirectX initialization error: " + ex.Message);
            }
        }

        private void SetupTransmitTimer()
        {
            transmitTimer = new System.Timers.Timer();
            transmitTimer.Interval = 20; // 20ms
            transmitTimer.Elapsed += TransmitTimer_Tick;
        }

        private void TransmitTimer_Tick(object? sender, ElapsedEventArgs e)
        {
            if (isConnected && serialPort != null)
            {
                try
                {
                    // Poll game controller on Windows
                    if (OperatingSystem.IsWindows())
                    {
                        PollGameController();
                    }

                    // Send command: SxxxTyyy
                    string command = $"S{steeringValue:D3}T{throttleValue:D3}\n";
                    serialPort.Write(command);
                    UpdateLatestMessageLabel($"S{steeringValue:D3}T{throttleValue:D3}");
                    // LogMessage($"Sent: {command.Trim()}");
                }
                catch (Exception ex)
                {
                    LogMessage("Transmission error: " + ex.Message);
                    Disconnect();
                }
            }
        }

        private void PollGameController()
        {
            if (joystick == null) return;

            try
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();

                // Steering from X-axis (left stick X or wheel)
                int steeringAxis = state.X;
                steeringValue = (int)MapRange(steeringAxis, 0, 65535, 0, 180);
                UpdateSteeringUI();

                // Throttle from Y-axis (right trigger or Y-axis)
                int throttleAxis = state.Y;
                throttleValue = (int)MapRange(throttleAxis, 0, 65535, 40, 140);
                UpdateThrottleUI();

            }
            catch (Exception ex)
            {
                LogMessage("Controller poll error: " + ex.Message);
            }
        }

        private double MapRange(double value, double fromMin, double fromMax, double toMin, double toMax)
        {
            return (value - fromMin) * (toMax - toMin) / (fromMax - fromMin) + toMin;
        }

        private void SteeringSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            var steeringNumeric = this.FindControl<NumericUpDown>("SteeringNumeric");
            if (steeringNumeric != null)
            {
                steeringValue = (int)((Slider)sender!).Value;
                steeringNumeric.Value = steeringValue;
            }
        }

        private void SteeringNumeric_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            var steeringSlider = this.FindControl<Slider>("SteeringSlider");
            if (steeringSlider != null)
            {
                steeringValue = (int)((NumericUpDown)sender!).Value;
                steeringSlider.Value = steeringValue;
            }
        }

        private void ThrottleSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            var throttleNumeric = this.FindControl<NumericUpDown>("ThrottleNumeric");
            if (throttleNumeric != null)
            {
                throttleValue = (int)e.NewValue;
                throttleNumeric.Value = throttleValue;
            }
        }

        private void ThrottleNumeric_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            var throttleSlider = this.FindControl<Slider>("ThrottleSlider");
            if (throttleSlider != null)
            {
                throttleValue = (int)e.NewValue;
                throttleSlider.Value = throttleValue;
            }
        }

        private void UpdateSteeringUI()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var steeringSlider = this.FindControl<Slider>("SteeringSlider");
                var steeringNumeric = this.FindControl<NumericUpDown>("SteeringNumeric");
                if (steeringSlider != null) steeringSlider.Value = steeringValue;
                if (steeringNumeric != null) steeringNumeric.Value = steeringValue;
            });
        }

        private void UpdateThrottleUI()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var throttleSlider = this.FindControl<Slider>("ThrottleSlider");
                var throttleNumeric = this.FindControl<NumericUpDown>("ThrottleNumeric");
                if (throttleSlider != null) throttleSlider.Value = throttleValue;
                if (throttleNumeric != null) throttleNumeric.Value = throttleValue;
            });
        }

        private void UpdateLatestMessageLabel(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var latestMessageLabel = this.FindControl<TextBlock>("LatestMessageLabel");
                if (latestMessageLabel != null)
                {
                    latestMessageLabel.Text = $"Latest: {message}";
                }
            });
        }

        private void UpdateLatestAckLabel(string ack)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var latestAckLabel = this.FindControl<TextBlock>("LatestAckLabel");
                if (latestAckLabel != null)
                {
                    latestAckLabel.Text = $"Ack: {ack}";
                }
            });
        }

        private void SerialPort_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = serialPort.ReadLine().Trim();
                UpdateLatestAckLabel(data);
                LogMessage($"Received: {data}");
                // if (data.StartsWith("OK:"))
                // {
                //     // This is an acknowledgement with current values
                //     // LogMessage($"Received ack: {data}");
                // }
                // else
                // {
                //     // Other data
                //     // LogMessage($"Received: {data}");
                // }
            }
            catch (Exception ex)
            {
                LogMessage("Serial read error: " + ex.Message);
            }
        }

        private void RefreshButton_Click(object? sender, RoutedEventArgs e)
        {
            RefreshPorts();
        }

        private void RefreshPorts()
        {
            var portComboBox = this.FindControl<ComboBox>("PortComboBox");
            if (portComboBox != null)
            {
                portComboBox.Items.Clear();
                string[] ports = SerialPort.GetPortNames();
                foreach (var port in ports)
                {
                    portComboBox.Items.Add(port);
                }

                if (ports.Length > 0)
                {
                    if (!string.IsNullOrEmpty(savedPort) && ports.Contains(savedPort))
                    {
                        portComboBox.SelectedItem = savedPort;
                    }
                    else
                    {
                        portComboBox.SelectedIndex = 0;
                    }
                }

                LogMessage($"Found {ports.Length} serial ports");
            }
        }

        private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!isConnected)
            {
                await Connect();
            }
            else
            {
                Disconnect();
            }
        }

        private async Task Connect()
        {
            var portComboBox = this.FindControl<ComboBox>("PortComboBox");
            var baudComboBox = this.FindControl<ComboBox>("BaudComboBox");
            var connectButton = this.FindControl<Button>("ConnectButton");
            var statusLabel = this.FindControl<TextBlock>("StatusLabel");

            if (portComboBox == null || baudComboBox == null || connectButton == null || statusLabel == null)
                return;

            try
            {
                if (portComboBox.SelectedItem == null)
                {
                    await ShowMessage("Please select a serial port", "Error");
                    return;
                }

                string portName = portComboBox.SelectedItem.ToString();
                string baudRateStr = (baudComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? baudComboBox.SelectedItem?.ToString() ?? "115200";

                serialPort = new SerialPort(portName, int.Parse(baudRateStr));
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();

                isConnected = true;
                connectButton.Content = "Disconnect";
                statusLabel.Text = "Connected";
                statusLabel.Classes.Clear();
                statusLabel.Classes.Add("green");
                transmitTimer?.Start();

                LogMessage($"Connected to {serialPort.PortName} at {serialPort.BaudRate} baud");
                SaveSettings();

            }
            catch (Exception ex)
            {
                LogMessage("Connection error: " + ex.Message);
                await ShowMessage("Failed to connect: " + ex.Message, "Error");
            }
        }

        private void Disconnect()
        {
            var connectButton = this.FindControl<Button>("ConnectButton");
            var statusLabel = this.FindControl<TextBlock>("StatusLabel");

            try
            {
                transmitTimer?.Stop();
                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.Close();
                }
                isConnected = false;
                if (connectButton != null) connectButton.Content = "Connect";
                if (statusLabel != null)
                {
                    statusLabel.Text = "Disconnected";
                    statusLabel.Classes.Clear();
                    statusLabel.Classes.Add("red");
                }

                UpdateLatestMessageLabel("(disconnected)");
                UpdateLatestAckLabel("(waiting)");

                LogMessage("Disconnected");
            }
            catch (Exception ex)
            {
                LogMessage("Disconnection error: " + ex.Message);
            }
        }

        private async Task ShowMessage(string message, string title)
        {
            var dialog = new Window
            {
                Title = title,
                Content = new TextBlock { Text = message, Margin = new Avalonia.Thickness(20) },
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await dialog.ShowDialog(this);
        }

        private void LogMessage(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var logTextBox = this.FindControl<TextBox>("LogTextBox");
                if (logTextBox != null)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    logTextBox.Text += $"[{timestamp}] {message}\n";

                    // Auto scroll to bottom
                    logTextBox.CaretIndex = logTextBox.Text.Length;

                    // Limit log size
                    var lines = logTextBox.Text.Split('\n');
                    if (lines.Length > 1000)
                    {
                        logTextBox.Text = string.Join('\n', lines.Skip(500));
                    }
                }
            });
        }

        private void LoadSettings()
        {
            try
            {
                var baudComboBox = this.FindControl<ComboBox>("BaudComboBox");
                if (File.Exists(SETTINGS_FILE))
                {
                    var lines = File.ReadAllLines(SETTINGS_FILE);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            switch (parts[0])
                            {
                                case "Port":
                                    savedPort = parts[1];
                                    break;
                                case "Baud":
                                    if (baudComboBox != null)
                                    {
                                        foreach (var item in baudComboBox.Items)
                                        {
                                            if (item is ComboBoxItem cbi && cbi.Content?.ToString() == parts[1])
                                            {
                                                baudComboBox.SelectedItem = cbi;
                                                break;
                                            }
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("Settings load error: " + ex.Message);
            }
        }

        private void SaveSettings()
        {
            try
            {
                var portComboBox = this.FindControl<ComboBox>("PortComboBox");
                var baudComboBox = this.FindControl<ComboBox>("BaudComboBox");

                var settings = new List<string>();
                if (portComboBox?.SelectedItem != null)
                    settings.Add($"Port={portComboBox.SelectedItem}");
                if (baudComboBox?.SelectedItem != null)
                {
                    string baudValue = (baudComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? baudComboBox.SelectedItem.ToString();
                    settings.Add($"Baud={baudValue}");
                }

                File.WriteAllLines(SETTINGS_FILE, settings);
            }
            catch (Exception ex)
            {
                LogMessage("Settings save error: " + ex.Message);
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            Disconnect();
            if (OperatingSystem.IsWindows())
            {
                if (joystick != null)
                {
                    joystick.Unacquire();
                    joystick.Dispose();
                }
                directInput?.Dispose();
            }
            base.OnClosing(e);
        }
    }
}
