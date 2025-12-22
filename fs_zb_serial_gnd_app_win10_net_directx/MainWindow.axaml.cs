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

        private EventWebSocketServer eventServer = new EventWebSocketServer();

        private CheckBox? partyDayModeToggle;
        private CheckBox? anyQrToggle;
        private ComboBox? scannerPortComboBox;
        private Button? scannerRefreshButton;
        private Button? scannerConnectButton;
        private TextBlock? scannerStateLabel;
        private TextBlock? sessionCountdownLabel;
        private Button? endSessionButton;
        private TextBox? manualQrTextBox;
        private Button? simulateQrButton;
        private TextBlock? membershipInfoLabel;

        private PartyDaySessionManager partyDaySessionManager = new PartyDaySessionManager();
        private QrScannerManager qrScannerManager = new QrScannerManager();
        private bool partyDayEnabled = false;
        private bool partyDayAnyQr = true;
        private string? partyDayScannerPort;
        private bool partyDayDebugEnabled = true;
        private bool neutralSentWhileLocked = false;
        private System.Timers.Timer? scannerReconnectTimer;
        private string? lastPartyDayMember;
        private string? lastPartyDayQr;
        private string? lastPartyDaySource;

        // Transmit Timer
        private System.Timers.Timer? transmitTimer;
        private System.Timers.Timer? autoConnectTimer;
        private int? lastBroadcastSteering;
        private int? lastBroadcastThrottle;
        private int? lastBroadcastBrake;
        private int? lastBroadcastSteeringRaw;
        private int? lastBroadcastThrottleRaw;
        private int? lastBroadcastBrakeRaw;

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
            SetupScannerReconnectTimer();
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
            partyDayModeToggle = this.FindControl<CheckBox>("PartyDayModeToggle");
            anyQrToggle = this.FindControl<CheckBox>("AnyQrToggle");
            scannerPortComboBox = this.FindControl<ComboBox>("ScannerPortComboBox");
            scannerRefreshButton = this.FindControl<Button>("ScannerRefreshButton");
            scannerConnectButton = this.FindControl<Button>("ScannerConnectButton");
            scannerStateLabel = this.FindControl<TextBlock>("ScannerStateLabel");
            sessionCountdownLabel = this.FindControl<TextBlock>("SessionCountdownLabel");
            endSessionButton = this.FindControl<Button>("EndSessionButton");
            manualQrTextBox = this.FindControl<TextBox>("ManualQrTextBox");
            simulateQrButton = this.FindControl<Button>("SimulateQrButton");
            membershipInfoLabel = this.FindControl<TextBlock>("MembershipInfoLabel");
            var partyDayDebugToggle = this.FindControl<CheckBox>("PartyDayDebugToggle");

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
            if (partyDayModeToggle != null) partyDayModeToggle.IsCheckedChanged += PartyDayModeToggle_IsCheckedChanged;
            if (anyQrToggle != null) anyQrToggle.IsCheckedChanged += AnyQrToggle_IsCheckedChanged;
            if (partyDayDebugToggle != null) partyDayDebugToggle.IsCheckedChanged += PartyDayDebugToggle_IsCheckedChanged;
            if (scannerRefreshButton != null) scannerRefreshButton.Click += ScannerRefreshButton_Click;
            if (scannerConnectButton != null) scannerConnectButton.Click += ScannerConnectButton_Click;
            if (endSessionButton != null) endSessionButton.Click += EndSessionButton_Click;
            if (simulateQrButton != null) simulateQrButton.Click += SimulateQrButton_Click;

            qrScannerManager.QrScanned += OnQrScanned;
            qrScannerManager.StatusChanged += OnScannerStatusChanged;
            partyDaySessionManager.SessionStarted += OnPartyDaySessionStarted;
            partyDaySessionManager.SessionEnded += OnPartyDaySessionEnded;
            partyDaySessionManager.Tick += OnPartyDaySessionTick;

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
            partyDayEnabled = settingsManager.PartyDayEnabled;
            partyDayAnyQr = settingsManager.PartyDayAnyQr;
            partyDayScannerPort = settingsManager.PartyDayScannerPort;
            partyDayDebugEnabled = settingsManager.PartyDayDebugEnabled;
            if (startIndexNumeric != null) startIndexNumeric.Value = settingsManager.StartIndex;
            if (endIndexNumeric != null) endIndexNumeric.Value = settingsManager.EndIndex;
            if (activeIndexNumeric != null)
            {
                var candidate = settingsManager.StartIndex <= settingsManager.EndIndex ? settingsManager.StartIndex : 0;
                activeIndexNumeric.Value = candidate;
            }

            if (partyDayModeToggle != null) partyDayModeToggle.IsChecked = partyDayEnabled;
            if (anyQrToggle != null) anyQrToggle.IsChecked = partyDayAnyQr;
            if (partyDayDebugToggle != null) partyDayDebugToggle.IsChecked = partyDayDebugEnabled;
            UpdatePartyDayStatus();
            RefreshScannerPorts();
            if (scannerPortComboBox != null && !string.IsNullOrWhiteSpace(partyDayScannerPort))
            {
                scannerPortComboBox.SelectedItem = partyDayScannerPort;
            }

            if (webSocketToggle != null) webSocketToggle.IsChecked = webSocketClientEnabled;
            if (reverseSteeringToggle != null) reverseSteeringToggle.IsChecked = reverseSteeringInput;
            if (reverseThrottleToggle != null) reverseThrottleToggle.IsChecked = reverseThrottleInput;
            if (autoConnectToggle != null) autoConnectToggle.IsChecked = autoConnectSerial;
            if (steeringOffsetNumeric != null) steeringOffsetNumeric.Value = steeringOffset;

            StartEventServer();
            // Now safe to refresh ports
            RefreshPorts();
            UpdateSteeringOffsetLabel();
            ApplyWebSocketEnabledState();
            UpdateAutoConnectTimerState();
        }

        private void StartEventServer()
        {
            try
            {
                eventServer.Start();
                LogMessage($"Event WS server: ws://localhost:{eventServer.Port}{eventServer.Path}");
            }
            catch (Exception ex)
            {
                LogMessage($"Event WS start failed: {ex.Message}");
            }
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

        private void SetupScannerReconnectTimer()
        {
            scannerReconnectTimer = new System.Timers.Timer(5000);
            scannerReconnectTimer.AutoReset = true;
            scannerReconnectTimer.Elapsed += ScannerReconnectTimer_Elapsed;
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

                if (partyDayEnabled && !partyDaySessionManager.SessionActive)
                {
                    if (!neutralSentWhileLocked)
                    {
                        serialManager.SendCommand(90, 90);
                        neutralSentWhileLocked = true;
                        UpdateLatestMessageLabel("Locked by PartyDay");
                    }
                    return;
                }

                neutralSentWhileLocked = false;

                serialManager.SendCommand(effectiveSteering, effectiveThrottle);
                UpdateLatestMessageLabel(serialManager.LatestMessage);
                TryBroadcastControlEvent(effectiveSteering, effectiveThrottle);
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

        private void PartyDayModeToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            partyDayEnabled = (sender as CheckBox)?.IsChecked ?? false;
            settingsManager.SaveSettings(partyDayEnabled: partyDayEnabled);
            if (!partyDayEnabled)
            {
                partyDaySessionManager.StopSession();
                neutralSentWhileLocked = false;
            }
            UpdatePartyDayStatus();
            UpdateScannerReconnectTimerState();
            BroadcastPartyDayState("mode-toggle");
        }

        private void AnyQrToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            partyDayAnyQr = (sender as CheckBox)?.IsChecked ?? true;
            settingsManager.SaveSettings(partyDayAnyQr: partyDayAnyQr);
            BroadcastPartyDayState("anyqr-toggle");
        }

        private void PartyDayDebugToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            partyDayDebugEnabled = (sender as CheckBox)?.IsChecked ?? true;
            settingsManager.SaveSettings(partyDayDebugEnabled: partyDayDebugEnabled);
            BroadcastPartyDayState("debug-toggle");
        }

        private void ScannerRefreshButton_Click(object? sender, RoutedEventArgs e)
        {
            RefreshScannerPorts();
        }

        private void ScannerConnectButton_Click(object? sender, RoutedEventArgs e)
        {
            if (qrScannerManager.IsConnected)
            {
                qrScannerManager.Disconnect();
                UpdateScannerStateLabel("Scanner: Disconnected");
                UpdatePartyDayStatus();
                UpdateScannerReconnectTimerState();
                return;
            }

            var port = scannerPortComboBox?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(port))
            {
                LogMessage("Select a scanner port first.");
                return;
            }

            if (qrScannerManager.Connect(port))
            {
                partyDayScannerPort = port;
                settingsManager.SaveSettings(partyDayScannerPort: partyDayScannerPort);
                UpdateScannerStateLabel($"Scanner: Connected ({port})");
            }
            else
            {
                UpdateScannerStateLabel($"Scanner: Failed ({port})");
            }

            UpdatePartyDayStatus();
            UpdateScannerReconnectTimerState();
        }

        private void EndSessionButton_Click(object? sender, RoutedEventArgs e)
        {
            partyDaySessionManager.StopSession();
        }

        private void SimulateQrButton_Click(object? sender, RoutedEventArgs e)
        {
            var payload = manualQrTextBox?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(payload))
            {
                payload = "SIMULATED-GUEST";
            }
            HandleQrPayload(payload, source: "Simulated");
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

        private void UpdateScannerReconnectTimerState()
        {
            if (scannerReconnectTimer == null)
            {
                return;
            }

            scannerReconnectTimer.Stop();

            if (!partyDayEnabled)
            {
                return;
            }

            scannerReconnectTimer.Start();
        }

        private void ScannerReconnectTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!partyDayEnabled || qrScannerManager.IsConnected)
            {
                return;
            }

            var port = scannerPortComboBox?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(port))
            {
                port = partyDayScannerPort;
            }

            if (string.IsNullOrWhiteSpace(port))
            {
                return;
            }

            if (qrScannerManager.Connect(port))
            {
                partyDayScannerPort = port;
                settingsManager.SaveSettings(partyDayScannerPort: partyDayScannerPort);
                UpdateScannerStateLabel($"Scanner: Connected ({port})");
                UpdatePartyDayStatus();
            }
        }

        private void UpdatePartyDayStatus()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (partyDayModeToggle != null)
                {
                    partyDayModeToggle.IsChecked = partyDayEnabled;
                }
                if (anyQrToggle != null)
                {
                    anyQrToggle.IsChecked = partyDayAnyQr;
                }
                if (scannerStateLabel != null)
                {
                    scannerStateLabel.Text = qrScannerManager.IsConnected ? "Scanner: Connected" : "Scanner: Disconnected";
                }
                if (scannerConnectButton != null)
                {
                    scannerConnectButton.Content = qrScannerManager.IsConnected ? "Disconnect" : "Connect";
                }
                if (sessionCountdownLabel != null)
                {
                    sessionCountdownLabel.Text = partyDaySessionManager.SessionActive
                        ? $"Remaining: {partyDaySessionManager.Remaining:mm\\:ss}"
                        : "Remaining: --";
                }
                var statusText = partyDayEnabled
                    ? (partyDaySessionManager.SessionActive ? "Status: Unlocked (timer running)" : "Status: Locked until QR scan")
                    : "Status: Disabled";
                if (this.FindControl<TextBlock>("PartyDayStatusLabel") is TextBlock statusLabel)
                {
                    statusLabel.Text = statusText;
                }
            });
        }

        private void RefreshScannerPorts()
        {
            if (scannerPortComboBox == null)
                return;

            scannerPortComboBox.Items.Clear();
            foreach (var port in qrScannerManager.GetAvailablePorts())
            {
                scannerPortComboBox.Items.Add(port);
            }

            if (!string.IsNullOrWhiteSpace(partyDayScannerPort) && scannerPortComboBox.Items.Contains(partyDayScannerPort))
            {
                scannerPortComboBox.SelectedItem = partyDayScannerPort;
            }
            else if (scannerPortComboBox.Items.Count > 0)
            {
                scannerPortComboBox.SelectedIndex = 0;
            }
        }

        private void UpdateScannerStateLabel(string text)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (scannerStateLabel != null)
                {
                    scannerStateLabel.Text = text;
                }
            });
        }

        private void OnScannerStatusChanged(string status)
        {
            UpdateScannerStateLabel(status);
            LogMessage(status);
            UpdatePartyDayStatus();
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (membershipInfoLabel != null)
                {
                    membershipInfoLabel.Text = status;
                }
            });
            BroadcastPartyDayState("scanner-status");
        }

        private void OnQrScanned(string payload)
        {
            HandleQrPayload(payload, source: "Scanner");
        }

        private void HandleQrPayload(string payload, string source)
        {
            var sanitized = string.IsNullOrWhiteSpace(payload) ? "(empty)" : payload.Trim();

            if (partyDayAnyQr)
            {
                StartSessionForMember($"Guest-{Math.Abs(sanitized.GetHashCode()) % 10000:D4}", sanitized, source);
                return;
            }

            // Placeholder for future API call. Currently treats any payload as valid.
            StartSessionForMember($"Guest-{Math.Abs(sanitized.GetHashCode()) % 10000:D4}", sanitized, source);
        }

        private void StartSessionForMember(string memberName, string payload, string source)
        {
            lastPartyDayMember = memberName;
            lastPartyDayQr = payload;
            lastPartyDaySource = source;
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (membershipInfoLabel != null)
                {
                    membershipInfoLabel.Text = $"Member: {memberName} | QR: {payload} | Via: {source} | Verified {DateTime.Now:HH:mm:ss}";
                }
            });

            partyDaySessionManager.StartSession();
            LogMessage($"PartyDay session started for {memberName} (source: {source}).");
            UpdatePartyDayStatus();
            BroadcastPartyDaySessionEvent("started", memberName, payload, source);
            BroadcastPartyDayState("session-started");
        }

        private void OnPartyDaySessionStarted()
        {
            neutralSentWhileLocked = false;
            UpdatePartyDayStatus();
        }

        private void OnPartyDaySessionEnded()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (membershipInfoLabel != null && partyDayEnabled)
                {
                    membershipInfoLabel.Text += " | Session ended";
                }
            });
            UpdatePartyDayStatus();
            BroadcastPartyDaySessionEvent("ended", lastPartyDayMember, lastPartyDayQr, lastPartyDaySource);
            BroadcastPartyDayState("session-ended");
        }

        private void OnPartyDaySessionTick(TimeSpan remaining)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (sessionCountdownLabel != null)
                {
                    sessionCountdownLabel.Text = $"Remaining: {remaining:mm\\:ss}";
                }
            });
            BroadcastPartyDaySessionEvent("tick", lastPartyDayMember, lastPartyDayQr, lastPartyDaySource);
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

            var effectiveSteering = Math.Clamp(ApplySteeringDirection(steeringValue) + steeringOffset, 0, 180);
            var effectiveThrottle = Math.Clamp(ApplyThrottleDirection(throttleValue), 0, 180);
            TryBroadcastControlEvent(effectiveSteering, effectiveThrottle);
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

            var effectiveSteering = Math.Clamp(ApplySteeringDirection(steeringValue) + steeringOffset, 0, 180);
            var effectiveThrottle = Math.Clamp(ApplyThrottleDirection(throttleValue), 0, 180);
            TryBroadcastControlEvent(effectiveSteering, effectiveThrottle);
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
            qrScannerManager.Dispose();
            partyDaySessionManager.Dispose();
            scannerReconnectTimer?.Stop();
            scannerReconnectTimer?.Dispose();
            autoConnectTimer?.Stop();
            autoConnectTimer?.Dispose();
            eventServer.Dispose();
            base.OnClosing(e);
        }

        private void TryBroadcastControlEvent(int steering180, int throttle180)
        {
            var rawSteeringValue = partyDayDebugEnabled
                ? (webSocketInputManager.LastRawSteering ?? MapToRange(steering180, 0, 180, 0, 65535))
                : (int?)null;
            var rawThrottleValue = partyDayDebugEnabled
                ? (webSocketInputManager.LastRawThrottle ?? MapToRange(throttle180, 0, 180, 0, 65535))
                : (int?)null;
            var rawBrakeValue = partyDayDebugEnabled
                ? (webSocketInputManager.LastRawBrake ?? 0)
                : (int?)null;
            var brake180 = rawBrakeValue.HasValue ? MapToRange(rawBrakeValue.Value, 0, 65535, 0, 180) : 0;

            if (lastBroadcastSteering == steering180 &&
                lastBroadcastThrottle == throttle180 &&
                lastBroadcastBrake == brake180 &&
                lastBroadcastSteeringRaw == rawSteeringValue &&
                lastBroadcastThrottleRaw == rawThrottleValue &&
                lastBroadcastBrakeRaw == rawBrakeValue)
            {
                return;
            }

            lastBroadcastSteering = steering180;
            lastBroadcastThrottle = throttle180;
            lastBroadcastBrake = brake180;
            lastBroadcastSteeringRaw = rawSteeringValue;
            lastBroadcastThrottleRaw = rawThrottleValue;
            lastBroadcastBrakeRaw = rawBrakeValue;

            var payload = new
            {
                version = "1.0",
                type = "control",
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                steering = steering180,
                throttle = throttle180,
                brake = brake180,
                steeringRaw = rawSteeringValue,
                throttleRaw = rawThrottleValue,
                brakeRaw = rawBrakeValue,
                debugEnabled = partyDayDebugEnabled
            };

            _ = eventServer.BroadcastAsync(payload);
        }

        private void BroadcastPartyDayState(string reason)
        {
            var payload = new
            {
                version = "1.0",
                type = "partyday.state",
                reason,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                modeEnabled = partyDayEnabled,
                sessionActive = partyDaySessionManager.SessionActive,
                remainingMs = (long)Math.Max(0, partyDaySessionManager.Remaining.TotalMilliseconds),
                anyQr = partyDayAnyQr,
                scannerConnected = qrScannerManager.IsConnected,
                scannerPort = partyDayScannerPort,
                debugEnabled = partyDayDebugEnabled
            };

            _ = eventServer.BroadcastAsync(payload);
        }

        private void BroadcastPartyDaySessionEvent(string action, string? member, string? qrPayload, string? source)
        {
            var payload = new
            {
                version = "1.0",
                type = "partyday.session",
                action,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                member,
                qrPayload,
                source,
                sessionSeconds = partyDaySessionManager.SessionDurationSeconds,
                remainingMs = (long)Math.Max(0, partyDaySessionManager.Remaining.TotalMilliseconds),
                modeEnabled = partyDayEnabled,
                anyQr = partyDayAnyQr,
                scannerConnected = qrScannerManager.IsConnected,
                scannerPort = partyDayScannerPort,
                debugEnabled = partyDayDebugEnabled
            };

            _ = eventServer.BroadcastAsync(payload);
        }

        private int MapToRange(int value, int fromMin, int fromMax, int toMin, int toMax)
        {
            double clamped = Math.Clamp(value, fromMin, fromMax);
            double scaled = (clamped - fromMin) * (toMax - toMin) / (fromMax - fromMin) + toMin;
            return (int)Math.Round(Math.Clamp(scaled, toMin, toMax));
        }
    }
}
