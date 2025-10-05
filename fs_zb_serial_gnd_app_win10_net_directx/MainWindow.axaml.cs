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
        // Managers
        private SerialManager serialManager;
        private GameControllerManager gameControllerManager;
        private SettingsManager settingsManager;

        // Control Values
        private int steeringValue = 90;  // 0-180
        private int throttleValue = 90;  // 1-180 (neutral)
        private bool isDrivingMode = false;
        private int steeringOffset = 0;

        // Transmit Timer
        private System.Timers.Timer? transmitTimer;

        public MainWindow()
        {
            InitializeComponent();
            serialManager = new SerialManager();
            gameControllerManager = new GameControllerManager();
            settingsManager = new SettingsManager();
            if (OperatingSystem.IsWindows())
            {
                gameControllerManager.Initialize();
            }
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
            var baudComboBox = this.FindControl<ComboBox>("BaudComboBox");
            var drivingModeToggle = this.FindControl<CheckBox>("DrivingModeToggle");
            var steeringOffsetNumeric = this.FindControl<NumericUpDown>("SteeringOffsetNumeric");

            // Wire up events
            if (steeringSlider != null) steeringSlider.ValueChanged += SteeringSlider_ValueChanged;
            if (steeringNumeric != null) steeringNumeric.ValueChanged += SteeringNumeric_ValueChanged;
            if (throttleSlider != null) throttleSlider.ValueChanged += ThrottleSlider_ValueChanged;
            if (throttleNumeric != null) throttleNumeric.ValueChanged += ThrottleNumeric_ValueChanged;
            if (refreshButton != null) refreshButton.Click += RefreshButton_Click;
            if (connectButton != null) connectButton.Click += ConnectButton_Click;
            if (drivingModeToggle != null) drivingModeToggle.IsCheckedChanged += DrivingModeToggle_IsCheckedChanged;
            if (steeringOffsetNumeric != null) steeringOffsetNumeric.ValueChanged += SteeringOffsetNumeric_ValueChanged;

            // Key handling
            this.KeyDown += OnKeyDown;
            this.KeyUp += OnKeyUp;

            // Subscribe to manager events
            serialManager.DataReceived += SerialManager_DataReceived;
            serialManager.TransmissionError += SerialManager_TransmissionError;
            gameControllerManager.ControlValuesChanged += GameControllerManager_ControlValuesChanged;

            // Load settings
            if (baudComboBox != null) settingsManager.LoadSettings(baudComboBox);

            // Now safe to refresh ports
            RefreshPorts();
            UpdateSteeringOffsetLabel();
        }

        private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.A:
                case Avalonia.Input.Key.Left:
                    if (isDrivingMode)
                    {
                        steeringValue = 120;
                    }
                    else
                    {
                        steeringValue = Math.Max(0, steeringValue - 5);
                    }
                    UpdateSteeringUI();
                    break;
                case Avalonia.Input.Key.D:
                case Avalonia.Input.Key.Right:
                    if (isDrivingMode)
                    {
                        steeringValue = 60;
                    }
                    else
                    {
                        steeringValue = Math.Min(180, steeringValue + 5);
                    }
                    UpdateSteeringUI();
                    break;
                case Avalonia.Input.Key.W:
                case Avalonia.Input.Key.Up:
                    if (isDrivingMode)
                    {
                        throttleValue = 108;
                    }
                    else
                    {
                        throttleValue = Math.Min(140, throttleValue + 1);
                    }
                    UpdateThrottleUI();
                    break;
                case Avalonia.Input.Key.S:
                case Avalonia.Input.Key.Down:
                    if (isDrivingMode)
                    {
                        throttleValue = 75;
                    }
                    else
                    {
                        throttleValue = Math.Max(40, throttleValue - 1);
                    }
                    UpdateThrottleUI();
                    break;
            }
        }

        private void OnKeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (isDrivingMode)
            {
                switch (e.Key)
                {
                    case Avalonia.Input.Key.A:
                    case Avalonia.Input.Key.Left:
                    case Avalonia.Input.Key.D:
                    case Avalonia.Input.Key.Right:
                        steeringValue = 90;
                        UpdateSteeringUI();
                        break;
                    case Avalonia.Input.Key.W:
                    case Avalonia.Input.Key.Up:
                    case Avalonia.Input.Key.S:
                    case Avalonia.Input.Key.Down:
                        throttleValue = 90;
                        UpdateThrottleUI();
                        break;
                }
            }
        }

        private void SteeringSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            var steeringNumeric = this.FindControl<NumericUpDown>("SteeringNumeric");
            if (steeringNumeric != null)
            {
                steeringValue = (int)((Slider)sender!).Value;
                steeringValue = Math.Clamp(steeringValue, 0, 180);
                steeringNumeric.Value = steeringValue;
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
            if (serialManager.IsConnected)
            {
                // Poll game controller on Windows
                if (OperatingSystem.IsWindows())
                {
                    gameControllerManager.Poll();
                }

                // Send command
                int effectiveSteering = Math.Clamp(steeringValue + steeringOffset, 0, 180);
                serialManager.SendCommand(effectiveSteering, throttleValue);
                UpdateLatestMessageLabel(serialManager.LatestMessage);
            }
        }

        private void SteeringNumeric_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            var steeringSlider = this.FindControl<Slider>("SteeringSlider");
            if (steeringSlider != null)
            {
                steeringValue = (int)((NumericUpDown)sender!).Value;
                steeringValue = Math.Clamp(steeringValue, 0, 180);
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

        private void DrivingModeToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                isDrivingMode = checkBox.IsChecked ?? false;
                // Reset throttle to neutral when toggling
                throttleValue = 90;
                UpdateThrottleUI();
            }
        }

        private void UpdateSteeringOffsetLabel()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var label = this.FindControl<TextBlock>("SteeringOffsetLabel");
                if (label != null)
                {
                    label.Text = $"({steeringOffset})";
                }
            });
        }

        private void SteeringOffsetNumeric_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            steeringOffset = (int)(e.NewValue ?? 0);
            UpdateSteeringOffsetLabel();
            UpdateSteeringUI();
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

        private void SerialManager_DataReceived(string data)
        {
            UpdateLatestAckLabel(data);
            LogMessage($"Received: {data}");
        }

        private void SerialManager_TransmissionError(string error)
        {
            LogMessage("Transmission error: " + error);
        }

        private void GameControllerManager_ControlValuesChanged(int steering, int throttle)
        {
            steeringValue = steering;
            throttleValue = throttle;
            UpdateSteeringUI();
            UpdateThrottleUI();
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
                string[] ports = serialManager.GetAvailablePorts();
                foreach (var port in ports)
                {
                    portComboBox.Items.Add(port);
                }

                if (ports.Length > 0)
                {
                    if (!string.IsNullOrEmpty(settingsManager.SavedPort) && ports.Contains(settingsManager.SavedPort))
                    {
                        portComboBox.SelectedItem = settingsManager.SavedPort;
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
            if (!serialManager.IsConnected)
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

                bool success = await serialManager.Connect(portName, int.Parse(baudRateStr));
                if (success)
                {
                    connectButton.Content = "Disconnect";
                    statusLabel.Text = "Connected";
                    statusLabel.Classes.Clear();
                    statusLabel.Classes.Add("green");
                    transmitTimer?.Start();

                    LogMessage($"Connected to {portName} at {baudRateStr} baud");
                    settingsManager.SaveSettings(portName, baudComboBox.SelectedItem);
                }
                else
                {
                    await ShowMessage("Failed to connect", "Error");
                }
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

            serialManager.Disconnect();
            transmitTimer?.Stop();
            if (connectButton != null) connectButton.Content = "Connect";
            if (statusLabel != null)
            {
                statusLabel.Text = "Disconnected";
                statusLabel.Classes.Clear();
                statusLabel.Classes.Add("red");
            }

            UpdateLatestMessageLabel(serialManager.LatestMessage);
            UpdateLatestAckLabel("(waiting)");

            LogMessage("Disconnected");
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

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            Disconnect();
            if (OperatingSystem.IsWindows())
            {
                gameControllerManager.Dispose();
            }
            base.OnClosing(e);
        }
    }
}
