using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SharpDX.DirectInput;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
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
        private WebSocketInputManager webSocketInputManager;

        // Control Values
        private int steeringValue = 90;  // 0-180
        private int throttleValue = 90;  // 0-180 (neutral)
        private bool isDrivingMode = false;
        private int steeringOffset = 0;
        private bool webSocketClientEnabled = true;
        private bool reverseSteeringInput = false;
        private bool reverseThrottleInput = false;
        private bool isUpdatingFromWebSocket = false;
        private bool autoConnectSerial = false;
        private bool connectInProgress = false;
        private NumericUpDown? startIndexNumeric;
        private NumericUpDown? endIndexNumeric;
        private NumericUpDown? activeIndexNumeric;
        private TextBlock? activeMacDisplayLabel;
        private Button? applyRangeButton;
        private Button? setActiveButton;
        private Button? requestActiveMacButton;

        // Transmit Timer
        private System.Timers.Timer? transmitTimer;
        private System.Timers.Timer? autoConnectTimer;

        public MainWindow()
        {
            InitializeComponent();
            serialManager = new SerialManager();
            gameControllerManager = new GameControllerManager();
            settingsManager = new SettingsManager();
            webSocketInputManager = new WebSocketInputManager();
            webSocketInputManager.ControlValuesChanged += WebSocketInputManager_ControlValuesChanged;
            webSocketInputManager.StatusChanged += WebSocketInputManager_StatusChanged;
            if (OperatingSystem.IsWindows())
            {
                gameControllerManager.Initialize();
            }
            SetupTransmitTimer();
            SetupAutoConnectTimer();
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
            var webSocketToggle = this.FindControl<CheckBox>("WebSocketToggle");
            var reverseSteeringToggle = this.FindControl<CheckBox>("ReverseSteeringToggle");
            var reverseThrottleToggle = this.FindControl<CheckBox>("ReverseThrottleToggle");
            var autoConnectToggle = this.FindControl<CheckBox>("AutoConnectToggle");
            startIndexNumeric = this.FindControl<NumericUpDown>("StartIndexNumeric");
            endIndexNumeric = this.FindControl<NumericUpDown>("EndIndexNumeric");
            activeIndexNumeric = this.FindControl<NumericUpDown>("ActiveIndexNumeric");
            activeMacDisplayLabel = this.FindControl<TextBlock>("ActiveMacDisplayLabel");
            applyRangeButton = this.FindControl<Button>("ApplyRangeButton");
            setActiveButton = this.FindControl<Button>("SetActiveButton");
            requestActiveMacButton = this.FindControl<Button>("RequestActiveMacButton");

            // Wire up events
            if (steeringSlider != null) steeringSlider.ValueChanged += SteeringSlider_ValueChanged;
            if (steeringNumeric != null) steeringNumeric.ValueChanged += SteeringNumeric_ValueChanged;
            if (throttleSlider != null) throttleSlider.ValueChanged += ThrottleSlider_ValueChanged;
            if (throttleNumeric != null) throttleNumeric.ValueChanged += ThrottleNumeric_ValueChanged;
            if (refreshButton != null) refreshButton.Click += RefreshButton_Click;
            if (connectButton != null) connectButton.Click += ConnectButton_Click;
            if (drivingModeToggle != null) drivingModeToggle.IsCheckedChanged += DrivingModeToggle_IsCheckedChanged;
            if (steeringOffsetNumeric != null) steeringOffsetNumeric.ValueChanged += SteeringOffsetNumeric_ValueChanged;
            if (webSocketToggle != null) webSocketToggle.IsCheckedChanged += WebSocketToggle_IsCheckedChanged;
            if (reverseSteeringToggle != null) reverseSteeringToggle.IsCheckedChanged += ReverseSteeringToggle_IsCheckedChanged;
            if (reverseThrottleToggle != null) reverseThrottleToggle.IsCheckedChanged += ReverseThrottleToggle_IsCheckedChanged;
            if (autoConnectToggle != null) autoConnectToggle.IsCheckedChanged += AutoConnectToggle_IsCheckedChanged;
            if (applyRangeButton != null) applyRangeButton.Click += ApplyRangeButton_Click;
            if (setActiveButton != null) setActiveButton.Click += SetActiveButton_Click;
            if (requestActiveMacButton != null) requestActiveMacButton.Click += RequestActiveMacButton_Click;

            // Key handling
            this.KeyDown += OnKeyDown;
            this.KeyUp += OnKeyUp;

            // Subscribe to manager events
            serialManager.DataReceived += SerialManager_DataReceived;
            serialManager.TransmissionError += SerialManager_TransmissionError;
            gameControllerManager.ControlValuesChanged += GameControllerManager_ControlValuesChanged;

            // Load settings
            if (baudComboBox != null) settingsManager.LoadSettings(baudComboBox);
            webSocketClientEnabled = settingsManager.WebSocketEnabled;
            reverseSteeringInput = settingsManager.ReverseSteeringInput;
            reverseThrottleInput = settingsManager.ReverseThrottleInput;
            autoConnectSerial = settingsManager.AutoConnectSerial;
            steeringOffset = settingsManager.SteeringOffset;
            if (startIndexNumeric != null) startIndexNumeric.Value = settingsManager.StartIndex;
            if (endIndexNumeric != null) endIndexNumeric.Value = settingsManager.EndIndex;
            if (activeIndexNumeric != null)
            {
                var candidate = settingsManager.StartIndex <= settingsManager.EndIndex ? settingsManager.StartIndex : 0;
                activeIndexNumeric.Value = candidate;
            }

            if (webSocketToggle != null) webSocketToggle.IsChecked = webSocketClientEnabled;
            if (reverseSteeringToggle != null) reverseSteeringToggle.IsChecked = reverseSteeringInput;
            if (reverseThrottleToggle != null) reverseThrottleToggle.IsChecked = reverseThrottleInput;
            if (autoConnectToggle != null) autoConnectToggle.IsChecked = autoConnectSerial;
            if (steeringOffsetNumeric != null) steeringOffsetNumeric.Value = steeringOffset;

            // Now safe to refresh ports
            RefreshPorts();
            UpdateSteeringOffsetLabel();
            ApplyWebSocketEnabledState();
            UpdateAutoConnectTimerState();
        }

        private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (webSocketClientEnabled)
                return;

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
                        throttleValue = Math.Min(180, throttleValue + 1);
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
                        throttleValue = Math.Max(0, throttleValue - 1);
                    }
                    UpdateThrottleUI();
                    break;
            }
        }

        private void OnKeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (webSocketClientEnabled)
                return;

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
            if (webSocketClientEnabled && !isUpdatingFromWebSocket)
                return;

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

        private void SetupAutoConnectTimer()
        {
            autoConnectTimer = new System.Timers.Timer(10000);
            autoConnectTimer.AutoReset = true;
            autoConnectTimer.Elapsed += AutoConnectTimer_Elapsed;
        }

        private void TransmitTimer_Tick(object? sender, ElapsedEventArgs e)
        {
            if (serialManager.IsConnected)
            {
                // Poll game controller on Windows
                if (OperatingSystem.IsWindows() && !webSocketClientEnabled)
                {
                    gameControllerManager.Poll();
                }

                // Send command
                int effectiveSteering = Math.Clamp(ApplySteeringDirection(steeringValue) + steeringOffset, 0, 180);
                int effectiveThrottle = Math.Clamp(ApplyThrottleDirection(throttleValue), 0, 180);

                serialManager.SendCommand(effectiveSteering, effectiveThrottle);
                UpdateLatestMessageLabel(serialManager.LatestMessage);
            }
        }

        private void AutoConnectTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!autoConnectSerial || serialManager.IsConnected || connectInProgress)
            {
                return;
            }

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!autoConnectSerial || serialManager.IsConnected)
                {
                    return;
                }

                await Connect(showErrors: false);
            });
        }

        private void SteeringNumeric_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            if (webSocketClientEnabled && !isUpdatingFromWebSocket)
                return;

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
            if (webSocketClientEnabled && !isUpdatingFromWebSocket)
                return;

            var throttleNumeric = this.FindControl<NumericUpDown>("ThrottleNumeric");
            if (throttleNumeric != null)
            {
                throttleValue = (int)e.NewValue;
                throttleValue = Math.Clamp(throttleValue, 0, 180);
                throttleNumeric.Value = throttleValue;
            }
        }

        private void ThrottleNumeric_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            if (webSocketClientEnabled && !isUpdatingFromWebSocket)
                return;

            var throttleSlider = this.FindControl<Slider>("ThrottleSlider");
            if (throttleSlider != null)
            {
                throttleValue = (int)e.NewValue;
                throttleValue = Math.Clamp(throttleValue, 0, 180);
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

        private void WebSocketToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            webSocketClientEnabled = (sender as CheckBox)?.IsChecked ?? true;
            ApplyWebSocketEnabledState();
            settingsManager.SaveSettings(websocketEnabled: webSocketClientEnabled);
        }

        private void ReverseSteeringToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            reverseSteeringInput = (sender as CheckBox)?.IsChecked ?? false;
            settingsManager.SaveSettings(reverseSteering: reverseSteeringInput);
        }

        private void ReverseThrottleToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            reverseThrottleInput = (sender as CheckBox)?.IsChecked ?? false;
            settingsManager.SaveSettings(reverseThrottle: reverseThrottleInput);
        }

        private void AutoConnectToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            autoConnectSerial = (sender as CheckBox)?.IsChecked ?? false;
            settingsManager.SaveSettings(autoConnectSerial: autoConnectSerial);
            UpdateAutoConnectTimerState();
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
            settingsManager.SaveSettings(steeringOffset: steeringOffset);
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

        private void UpdateWebSocketStatus(string status)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var statusLabel = this.FindControl<TextBlock>("WebSocketStatusLabel");
                if (statusLabel != null)
                {
                    statusLabel.Text = $"WebSocket: {status}";
                }
            });
        }

        private void ApplyWebSocketEnabledState()
        {
            if (webSocketClientEnabled)
            {
                UpdateWebSocketStatus("Enabled (connecting...)");
                webSocketInputManager.Start();
            }
            else
            {
                webSocketInputManager.Stop();
                UpdateWebSocketStatus("Disabled");
            }
        }

        private void UpdateAutoConnectTimerState()
        {
            if (autoConnectTimer == null)
            {
                return;
            }

            autoConnectTimer.Stop();
            if (!autoConnectSerial)
            {
                return;
            }

            autoConnectTimer.Start();
            if (!serialManager.IsConnected && !connectInProgress)
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (!autoConnectSerial || serialManager.IsConnected)
                    {
                        return;
                    }

                    await Connect(showErrors: false);
                });
            }
        }

        private void ApplyRangeButton_Click(object? sender, RoutedEventArgs e)
        {
            ApplyRangeToGround(userTriggered: true);
        }

        private void SetActiveButton_Click(object? sender, RoutedEventArgs e)
        {
            SetActiveIndexOnGround(userTriggered: true);
        }

        private void RequestActiveMacButton_Click(object? sender, RoutedEventArgs e)
        {
            RequestActiveMacFromGround(logIfDisconnected: true);
        }

        private bool TryGetRange(out int start, out int end)
        {
            start = (int)(startIndexNumeric?.Value ?? 0);
            end = (int)(endIndexNumeric?.Value ?? 0);
            if (start < 0) start = 0;
            if (end < 0) end = 0;
            return start <= end && end <= 255;
        }

        private int GetActiveIndex()
        {
            var idx = (int)(activeIndexNumeric?.Value ?? 0);
            if (idx < 0) idx = 0;
            if (idx > 255) idx = 255;
            return idx;
        }

        private void ApplyRangeToGround(bool userTriggered = false)
        {
            if (!TryGetRange(out var start, out var end))
            {
                if (userTriggered)
                {
                    LogMessage("Invalid range. Ensure start <= end and both within 0-255.");
                }
                return;
            }

            settingsManager.SaveSettings(startIndex: start, endIndex: end);

            if (!serialManager.IsConnected)
            {
                if (userTriggered)
                {
                    LogMessage("Connect to a ground station before applying the range.");
                }
                return;
            }

            serialManager.SendMacRange(start, end);
            LogMessage($"Sent MAC index range {start}-{end} to ground station.");
            RequestActiveMacFromGround();
        }

        private void SetActiveIndexOnGround(bool userTriggered = false)
        {
            if (!TryGetRange(out var start, out var end))
            {
                if (userTriggered)
                {
                    LogMessage("Set a valid range before selecting an active index.");
                }
                return;
            }

            var index = GetActiveIndex();
            if (index < start || index > end)
            {
                LogMessage($"Active index must be within {start}-{end}.");
                return;
            }

            settingsManager.SaveSettings(startIndex: start, endIndex: end);

            if (!serialManager.IsConnected)
            {
                if (userTriggered)
                {
                    LogMessage("Connect to a ground station before selecting an active index.");
                }
                return;
            }

            serialManager.SendMacSelect(index);
            LogMessage($"Requested active index {index}.");
            RequestActiveMacFromGround();
        }

        private void RequestActiveMacFromGround(bool logIfDisconnected = false)
        {
            if (serialManager.IsConnected)
            {
                serialManager.RequestActiveMac();
            }
            else if (logIfDisconnected)
            {
                LogMessage("Ground station not connected; cannot refresh active MAC.");
            }
        }

        private void HandleActiveMacMessage(string data)
        {
            var parts = data.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                return;

            if (!int.TryParse(parts[1], out var index))
                return;
            var mac = parts[2];
            var total = 0;
            _ = int.TryParse(parts[3], out total);
            UpdateActiveMacLabel(index, mac, total);
        }

        private void UpdateActiveMacLabel(int index, string mac, int total)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (activeMacDisplayLabel != null)
                {
                    var humanIndex = (total <= 0) ? 0 : index + 1;
                    activeMacDisplayLabel.Text = total > 0
                        ? $"Active MAC [{humanIndex}/{total}]: {mac}"
                        : "Active MAC: (none)";
                }
            });
        }

        private void WebSocketInputManager_StatusChanged(string status)
        {
            UpdateWebSocketStatus(status);
            LogMessage($"WebSocket: {status}");
        }

        private void WebSocketInputManager_ControlValuesChanged(int steering, int throttle)
        {
            isUpdatingFromWebSocket = true;
            steeringValue = Math.Clamp(steering, 0, 180);
            throttleValue = Math.Clamp(throttle, 0, 180);
            UpdateSteeringUI();
            UpdateThrottleUI();
            isUpdatingFromWebSocket = false;
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
            if (string.IsNullOrWhiteSpace(data))
                return;

            if (data.StartsWith("MACACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                HandleActiveMacMessage(data);
                return;
            }

            if (data.StartsWith("MACRANGE-ACK", StringComparison.OrdinalIgnoreCase) ||
                data.StartsWith("RX:", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage(data);
                return;
            }

            UpdateLatestAckLabel(data);
            LogMessage($"Received: {data}");
        }

        private void SerialManager_TransmissionError(string error)
        {
            LogMessage("Transmission error: " + error);
        }

        private void GameControllerManager_ControlValuesChanged(int steering, int throttle)
        {
            if (webSocketClientEnabled)
                return;

            steeringValue = Math.Clamp(steering, 0, 180);
            throttleValue = Math.Clamp(throttle, 0, 180);
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

        private async Task Connect(bool showErrors = true)
        {
            var portComboBox = this.FindControl<ComboBox>("PortComboBox");
            var baudComboBox = this.FindControl<ComboBox>("BaudComboBox");
            var connectButton = this.FindControl<Button>("ConnectButton");
            var statusLabel = this.FindControl<TextBlock>("StatusLabel");

            if (connectInProgress || portComboBox == null || baudComboBox == null || connectButton == null || statusLabel == null)
            {
                return;
            }

            connectInProgress = true;
            try
            {
                if (portComboBox.SelectedItem == null)
                {
                    if (showErrors)
                    {
                        await ShowMessage("Please select a serial port", "Error");
                    }
                    else
                    {
                        LogMessage("Auto-connect skipped: no serial port selected.");
                    }
                    return;
                }

                string portName = portComboBox.SelectedItem.ToString();
                string baudRateStr = (baudComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? baudComboBox.SelectedItem?.ToString() ?? "115200";

                bool success = await serialManager.Connect(portName, int.Parse(baudRateStr));
                if (success)
                {
                    connectButton.Content = "Disconnect";
                    statusLabel.Text = "ENGINE: ON";
                    statusLabel.Classes.Clear();
                    statusLabel.Classes.Add("green");
                    transmitTimer?.Start();

                    LogMessage($"Connected to {portName} at {baudRateStr} baud");
                    settingsManager.SaveSettings(portName, baudComboBox.SelectedItem);
                    ApplyRangeToGround();
                    SetActiveIndexOnGround();
                    RequestActiveMacFromGround();
                }
                else
                {
                    if (showErrors)
                    {
                        await ShowMessage("Failed to connect", "Error");
                    }
                    else
                    {
                        LogMessage($"Auto-connect failed for {portName} at {baudRateStr} baud.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("Connection error: " + ex.Message);
                if (showErrors)
                {
                    await ShowMessage("Failed to connect: " + ex.Message, "Error");
                }
            }
            finally
            {
                connectInProgress = false;
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
                statusLabel.Text = "ENGINE: OFF";
                statusLabel.Classes.Clear();
                statusLabel.Classes.Add("red");
            }

            UpdateLatestMessageLabel(serialManager.LatestMessage);
            UpdateLatestAckLabel("(waiting)");
            UpdateActiveMacLabel(-1, "(none)", 0);

            LogMessage("Disconnected");
        }

        private async Task ShowMessage(string message, string title)
        {
            var dialog = new Window
            {
                Title = title,
                Content = new TextBlock { Text = message, Margin = new Avalonia.Thickness(20) },
                Width = 300,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await dialog.ShowDialog(this);
        }

        private void LogMessage(string message)
        {
            // Dispatcher.UIThread.InvokeAsync(() =>
            // {
            //     var logTextBox = this.FindControl<TextBox>("LogTextBox");
            //     if (logTextBox != null)
            //     {
            //         string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            //         logTextBox.Text += $"[{timestamp}] {message}\n";

            //         // Auto scroll to bottom
            //         logTextBox.CaretIndex = logTextBox.Text.Length;

            //         // Limit log size
            //         var lines = logTextBox.Text.Split('\n');
            //         if (lines.Length > 1000)
            //         {
            //             logTextBox.Text = string.Join('\n', lines.Skip(500));
            //         }
            //     }
            // });
        }

        private int ApplySteeringDirection(int value)
        {
            return reverseSteeringInput ? 180 - value : value;
        }

        private int ApplyThrottleDirection(int value)
        {
            return reverseThrottleInput ? 180 - value : value;
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            Disconnect();
            if (OperatingSystem.IsWindows())
            {
                gameControllerManager.Dispose();
            }
            webSocketInputManager.Dispose();
            autoConnectTimer?.Stop();
            autoConnectTimer?.Dispose();
            base.OnClosing(e);
        }
    }
}
