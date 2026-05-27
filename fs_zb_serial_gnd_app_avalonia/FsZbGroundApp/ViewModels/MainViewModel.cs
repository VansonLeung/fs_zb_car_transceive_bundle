using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsZbGroundApp.Services;
using LibVLCSharp.Shared;

namespace FsZbGroundApp.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogEntries = 300;

    private static readonly HashSet<string> SupportedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "file",
        "rtsp",
        "http",
        "https",
        "udp",
        "rtp"
    };

    private static readonly Regex CounterTripletPattern = new(
        @"(?i)rtp\D+(?<rtp>\d+)\D+wfb\D+(?<wfb>\d+)\D+wifi\D+(?<wifi>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex SingleCounterPattern = new(
        @"(?i)\b(?<key>rtp|wfb|wifi)(?:pktcount|framecount)?\b\D*(?<value>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex PortPattern = new(
        @"(?i)\b(playerport|port)\b\D*(?<port>\d{2,5})",
        RegexOptions.Compiled);

    private LibVLC? _libVlc;
    private readonly WfbSerialClient _wfbSerial;
    private readonly NativeWfbLibusbBridge _nativeWfbBridge;
    private readonly AppLaunchOptions _launchOptions;
    private readonly CancellationTokenSource _deviceRefreshLoopCts = new();
    private CancellationTokenSource? _nativeStatusLoopCts;
    private Task? _nativeStatusLoopTask;
    private CancellationTokenSource? _lowLatencyPlaybackCts;
    private Task? _lowLatencyPlaybackTask;
    private FfmpegLowLatencyDecoder? _lowLatencyDecoder;
    private readonly string? _automationLogPath;
    private readonly bool _consoleLoggingEnabled;
    private string? _lastSerialRefreshSummary;
    private string? _lastNativeDiagnostic;
    private string? _lastNativePlaybackUrl;
    private int _nativeStatusPollFailureCount;
    private bool _nativeStatusPollConnectedLogged;
    private bool _nativeWifiSeenLogged;
    private bool _nativeWfbSeenLogged;
    private bool _nativeMatchedLogged;
    private bool _nativeSessionKeySeenLogged;
    private bool _nativeSessionReadyLogged;
    private bool _nativeRtpFlowLogged;
    private int _lowLatencyUiUpdatePending;
    private DecodedVideoFrame? _pendingLowLatencyFrame;
    private WriteableBitmap? _lowLatencyFrameBufferA;
    private WriteableBitmap? _lowLatencyFrameBufferB;
    private bool _lowLatencyPresentToBufferA = true;
    private bool _disposed;
    private bool _playerInitialized;

    public MainViewModel(AppLaunchOptions? launchOptions = null)
    {
        _launchOptions = launchOptions ?? AppLaunchOptions.Default;
        _consoleLoggingEnabled = _launchOptions.EnableConsoleLogging;
        _automationLogPath = _consoleLoggingEnabled
            ? Path.Combine(Path.GetTempPath(), "fs_zb_ground_app_automation.log")
            : null;
        _wfbSerial = new WfbSerialClient();
        _nativeWfbBridge = new NativeWfbLibusbBridge();

        _wfbSerial.StatusChanged += status => Dispatcher.UIThread.Post(() =>
        {
            WfbSerialStatus = status;
            Log(InferLogLevel(status), status);
        });
        _wfbSerial.ConnectionStateChanged += connected => Dispatcher.UIThread.Post(() => WfbSerialConnected = connected);
        _wfbSerial.LineReceived += line => Dispatcher.UIThread.Post(() => HandleWfbSerialLine(line));

        StatusMessage = "Ready. Select adapter, configure channel, then press START.";

        if (!string.IsNullOrWhiteSpace(_automationLogPath))
        {
            try
            {
                File.WriteAllText(_automationLogPath, string.Empty);
            }
            catch
            {
                // Ignore temp log initialization failures.
            }
        }

        Log("info", "Ground app initialized.");
        Log("info", "Startup heavy work is deferred; adapter scan and native bridge probe run asynchronously.");
    }

    public async Task InitializeAsync()
    {
        await RefreshSerialPortsCoreAsync(initialLoad: true);
        await ProbeNativeBridgeAsync();
    }

    public async Task<bool> RunStartupAutomationAsync(AppLaunchOptions options)
    {
        if (!options.AutoStartWfb)
        {
            return true;
        }

        if (options.Channel is int channel)
        {
            SelectedChannel = channel.ToString();
        }

        if (!string.IsNullOrWhiteSpace(options.ChannelWidth))
        {
            SelectedChannelWidth = options.ChannelWidth;
        }

        if (!string.IsNullOrWhiteSpace(options.Codec))
        {
            SelectedCodec = options.Codec;
        }

        if (!string.IsNullOrWhiteSpace(options.KeyPath))
        {
            WfbKeyPath = options.KeyPath;
        }

        if (!string.IsNullOrWhiteSpace(options.VidPid))
        {
            var automationPort = AvailableSerialPorts.FirstOrDefault(item =>
                item.VidPid.Equals(options.VidPid, StringComparison.OrdinalIgnoreCase));

            if (automationPort is null)
            {
                WfbSerialStatus = $"Automation could not find adapter {options.VidPid}.";
                Log("error", WfbSerialStatus);
                return false;
            }

            SelectedSerialPort = automationPort;
        }

        Log(
            "info",
            $"Automation start: vidpid={options.VidPid ?? ResolveVidPidForNativeStart() ?? "AUTO"}, channel={SelectedChannel}, width={SelectedChannelWidth}, codec={SelectedCodec}.");

        await StartWfbSessionAsync();
        return WfbSessionStarted;
    }

    private void EnsureMediaPlayerInitialized()
    {
        if (_playerInitialized)
        {
            return;
        }

        Core.Initialize();

        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--no-audio",
            "--clock-jitter=0",
            "--clock-synchro=0",
            "--drop-late-frames",
            "--skip-frames",
            "--avcodec-fast");
        MediaPlayer = new MediaPlayer(_libVlc);

        MediaPlayer.Playing += (_, _) =>
        {
            PostStatus("Live stream connected.", true);
            Log("info", "Player connected to the live stream.");
        };
        MediaPlayer.Stopped += (_, _) =>
        {
            PostStatus("Stream stopped.", false);
            Log("warn", "Player stream stopped.");
        };
        MediaPlayer.EndReached += (_, _) =>
        {
            PostStatus("Stream ended.", false);
            Log("warn", "Player stream reached end.");
        };
        MediaPlayer.EncounteredError += (_, _) =>
        {
            PostStatus("Player error. Check URL and network.", false);
            Log("error", "Player encountered an error.");
        };

        _playerInitialized = true;
        Log("debug", "Media player initialized on demand.");
    }

    private void ApplyPlaybackLatencyOptions(Media media, Uri streamUri)
    {
        var cacheMs = Math.Clamp(NetworkCachingMs, 10, 2000);

        media.AddOption($":network-caching={cacheMs}");
        media.AddOption($":live-caching={cacheMs}");
        media.AddOption($":file-caching={cacheMs}");
        media.AddOption($":udp-caching={cacheMs}");
        media.AddOption($":rtp-caching={cacheMs}");
        media.AddOption(":clock-jitter=0");
        media.AddOption(":clock-synchro=0");
        media.AddOption(":drop-late-frames");
        media.AddOption(":skip-frames");

        if (streamUri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            Log("debug", $"Applying low-latency SDP playback profile with {cacheMs} ms cache budget.");
        }
    }

    private bool ShouldUseLowLatencyFramePlayback(Uri streamUri)
    {
        return streamUri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetExtension(streamUri.LocalPath), ".sdp", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryStartLowLatencyFramePlayback(Uri streamUri)
    {
        if (!ShouldUseLowLatencyFramePlayback(streamUri))
        {
            return false;
        }

        var inputPath = streamUri.LocalPath;
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            Log("warn", "Low-latency renderer skipped because the SDP file is unavailable.");
            return false;
        }

        StopLowLatencyPlayback();

        FfmpegLowLatencyDecoder? decoder = null;
        try
        {
            decoder = new FfmpegLowLatencyDecoder();
            if (!decoder.TryOpen(inputPath))
            {
                var openError = string.IsNullOrWhiteSpace(decoder.LastError)
                    ? $"FFmpeg could not open '{inputPath}'."
                    : decoder.LastError;

                decoder.Dispose();
                Log("warn", $"Low-latency frame renderer could not open the SDP stream via FFmpeg: {openError} Falling back to VLC playback.");
                return false;
            }

            Log("debug", "Low-latency frame renderer opened SDP via direct FFmpeg decode.");

            if (MediaPlayer?.IsPlaying == true)
            {
                MediaPlayer.Stop();
            }

            _lowLatencyDecoder = decoder;
            _lowLatencyPlaybackCts = new CancellationTokenSource();
            _lowLatencyPlaybackTask = Task.Run(() => RunLowLatencyPlaybackLoopAsync(decoder, _lowLatencyPlaybackCts.Token));
            IsLowLatencyPlaybackActive = true;
            IsVlcPlaybackVisible = false;
            IsConnected = false;
            StatusMessage = "Connecting low-latency frame renderer...";
            Log("info", "Starting direct FFmpeg low-latency frame renderer for the native WFB stream.");
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                decoder?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors while falling back to VLC.
            }

            Log("warn", $"Low-latency frame renderer failed to start via FFmpeg: {ex.Message}. Falling back to VLC playback.");
            return false;
        }
    }

    private async Task RunLowLatencyPlaybackLoopAsync(FfmpegLowLatencyDecoder decoder, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!decoder.TryReadFrame(out var frame) || frame is null)
                {
                    if (!string.IsNullOrWhiteSpace(decoder.LastError)
                        && !string.Equals(decoder.LastError, "FFmpeg decode loop was canceled.", StringComparison.Ordinal))
                    {
                        Log("debug", $"Low-latency frame renderer decode loop waiting: {decoder.LastError}");
                    }

                    await Task.Delay(5, cancellationToken);
                    continue;
                }

                QueueLowLatencyFrameForPresentation(frame);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown or reconnect.
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Log("warn", $"Low-latency frame renderer stopped unexpectedly: {ex.Message}. Use Disconnect and reconnect to retry.");
                StopLowLatencyPlayback();
            });
        }
    }

    private void QueueLowLatencyFrameForPresentation(DecodedVideoFrame frame)
    {
        var previousFrame = Interlocked.Exchange(ref _pendingLowLatencyFrame, frame);
        previousFrame?.Dispose();

        if (Interlocked.Exchange(ref _lowLatencyUiUpdatePending, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(ProcessPendingLowLatencyFrame, DispatcherPriority.Render);
    }

    private void ProcessPendingLowLatencyFrame()
    {
        try
        {
            while (true)
            {
                var frame = Interlocked.Exchange(ref _pendingLowLatencyFrame, null);
                if (frame is null)
                {
                    break;
                }

                try
                {
                    PresentLowLatencyFrame(frame);
                }
                finally
                {
                    frame.Dispose();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _lowLatencyUiUpdatePending, 0);

            if (Volatile.Read(ref _pendingLowLatencyFrame) is not null
                && Interlocked.Exchange(ref _lowLatencyUiUpdatePending, 1) == 0)
            {
                Dispatcher.UIThread.Post(ProcessPendingLowLatencyFrame, DispatcherPriority.Render);
            }
        }
    }

    private void PresentLowLatencyFrame(DecodedVideoFrame frame)
    {
        var buffer = frame.Buffer;
        var width = frame.Width;
        var height = frame.Height;
        var stride = frame.Stride;

        EnsureLowLatencyFrameBuffers(width, height);

        var targetBitmap = _lowLatencyPresentToBufferA ? _lowLatencyFrameBufferA : _lowLatencyFrameBufferB;
        if (targetBitmap is null)
        {
            return;
        }

        using var locked = targetBitmap.Lock();
        if (locked.RowBytes == stride)
        {
            Marshal.Copy(buffer, 0, locked.Address, frame.BufferLength);
        }
        else
        {
            var sourceOffset = 0;
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(buffer, sourceOffset, IntPtr.Add(locked.Address, row * locked.RowBytes), Math.Min(stride, locked.RowBytes));
                sourceOffset += stride;
            }
        }

        _lowLatencyPresentToBufferA = !_lowLatencyPresentToBufferA;
        LowLatencyFrameImage = targetBitmap;

        if (!IsConnected)
        {
            IsConnected = true;
            PostStatus("Live stream connected.", true);
            Log("info", "Low-latency frame renderer connected to the live stream.");
        }
    }

    private bool StopLowLatencyPlayback()
    {
        var hadActivePlayback = IsLowLatencyPlaybackActive || _lowLatencyPlaybackCts is not null || _lowLatencyDecoder is not null;

        var cts = _lowLatencyPlaybackCts;
        _lowLatencyPlaybackCts = null;
        var decoder = _lowLatencyDecoder;
        _lowLatencyDecoder = null;
        _lowLatencyPlaybackTask = null;

        decoder?.CancelPendingRead();

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
                // Ignore cancellation errors while stopping playback.
            }
        }

        try
        {
            decoder?.Dispose();
        }
        catch
        {
            // Ignore shutdown-time decoder disposal errors.
        }

        cts?.Dispose();
        IsLowLatencyPlaybackActive = false;
        IsVlcPlaybackVisible = true;
        Interlocked.Exchange(ref _lowLatencyUiUpdatePending, 0);
        Interlocked.Exchange(ref _pendingLowLatencyFrame, null)?.Dispose();

        if (hadActivePlayback)
        {
            LowLatencyFrameImage = null;
            _lowLatencyFrameBufferA?.Dispose();
            _lowLatencyFrameBufferB?.Dispose();
            _lowLatencyFrameBufferA = null;
            _lowLatencyFrameBufferB = null;
            LowLatencyFrameImage = null;
            _lowLatencyPresentToBufferA = true;
        }

        return hadActivePlayback;
    }

    private void EnsureLowLatencyFrameBuffers(int width, int height)
    {
        if (_lowLatencyFrameBufferA is not null
            && _lowLatencyFrameBufferB is not null
            && _lowLatencyFrameBufferA.PixelSize.Width == width
            && _lowLatencyFrameBufferA.PixelSize.Height == height
            && _lowLatencyFrameBufferB.PixelSize.Width == width
            && _lowLatencyFrameBufferB.PixelSize.Height == height)
        {
            return;
        }

        _lowLatencyFrameBufferA?.Dispose();
        _lowLatencyFrameBufferB?.Dispose();

        _lowLatencyFrameBufferA = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        _lowLatencyFrameBufferB = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        _lowLatencyPresentToBufferA = true;
        LowLatencyFrameImage = null;
    }

    private async Task ProbeNativeBridgeAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var availability = await Task.Run(() => (_nativeWfbBridge.IsAvailable, _nativeWfbBridge.AvailabilityMessage));
        stopwatch.Stop();

        Log(
            availability.IsAvailable ? "info" : "warn",
            $"Native libusb bridge status: {availability.AvailabilityMessage} (probe {stopwatch.ElapsedMilliseconds} ms).");
    }

    public ObservableCollection<string> ChannelOptions { get; } = new()
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13",
        "32", "36", "40", "44", "48", "52", "56", "60", "64", "68", "96", "100", "104",
        "108", "112", "116", "120", "124", "128", "132", "136", "140", "144", "149", "153",
        "157", "161", "169", "173", "177"
    };

    public ObservableCollection<string> ChannelWidthOptions { get; } = new()
    {
        "20", "40", "80", "160", "80_80", "5", "10", "MAX"
    };

    public ObservableCollection<string> CodecOptions { get; } = new()
    {
        "AUTO", "H264", "H265"
    };

    public ObservableCollection<WfbSerialPortInfo> AvailableSerialPorts { get; } = new();

    public ObservableCollection<GroundLogEntry> DriverLogs { get; } = new();

    public string PacketCountersText => $"{RtpPacketCount}/{WfbFrameCount}/{WifiFrameCount}";

    public string WfbStartButtonText => WfbSessionStarted ? "STOP" : "START";

    public string SelectedSerialAddressText
    {
        get
        {
            if (SelectedSerialPort is null)
            {
                return "Address: -";
            }

            var compat = SelectedSerialPort.IsRtl8812AuCompatible ? "RTL8812AU-COMPAT" : "GENERIC";
            var friendly = string.IsNullOrWhiteSpace(SelectedSerialPort.FriendlyName)
                ? string.Empty
                : $" | {SelectedSerialPort.FriendlyName}";

            var transport = SelectedSerialPort.SupportsSerialConnection
                ? $" | COM {SelectedSerialPort.SerialPortName}"
                : " | native libusb candidate";

            return $"Address: {SelectedSerialPort.Address} | VID:PID {SelectedSerialPort.VidPid} | {compat}{transport}{friendly}";
        }
    }

    [ObservableProperty]
    private MediaPlayer? _mediaPlayer;

    [ObservableProperty]
    private WriteableBitmap? _lowLatencyFrameImage;

    [ObservableProperty]
    private bool _isLowLatencyPlaybackActive;

    [ObservableProperty]
    private bool _isVlcPlaybackVisible = true;

    [ObservableProperty]
    private string _streamUrl = "udp://@:52356";

    [ObservableProperty]
    private int _networkCachingMs = 120;

    [ObservableProperty]
    private bool _useRtspTcp = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusMessage = "Disconnected";

    [ObservableProperty]
    private string _selectedChannel = "149";

    [ObservableProperty]
    private string _selectedChannelWidth = "20";

    [ObservableProperty]
    private string _selectedCodec = "AUTO";

    [ObservableProperty]
    private string _wfbKeyPath = "gs.key";

    [ObservableProperty]
    private WfbSerialPortInfo? _selectedSerialPort;

    [ObservableProperty]
    private bool _wfbSerialConnected;

    [ObservableProperty]
    private string _wfbSerialStatus = "WFB serial disconnected.";

    [ObservableProperty]
    private string _wfbSerialLastMessage = "-";

    [ObservableProperty]
    private bool _wfbSessionStarted;

    [ObservableProperty]
    private long _rtpPacketCount;

    [ObservableProperty]
    private long _wfbFrameCount;

    [ObservableProperty]
    private long _wifiFrameCount;

    [RelayCommand]
    private Task RefreshSerialPortsAsync()
        => RefreshSerialPortsCoreAsync();

    private async Task RefreshSerialPortsCoreAsync(bool initialLoad = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var previousSelection = SelectedSerialPort;
        var previousSelectionKey = CreateSelectionKey(previousSelection);

        var refreshedPorts = await Task.Run(() => _wfbSerial.GetAvailablePorts());
        stopwatch.Stop();

        AvailableSerialPorts.Clear();
        foreach (var port in refreshedPorts)
        {
            AvailableSerialPorts.Add(port);
        }

        var matchedSelection = previousSelectionKey is null
            ? null
            : FindPortByKey(previousSelectionKey);

        if (previousSelection is not null && matchedSelection is null)
        {
            if (WfbSerialConnected)
            {
                _wfbSerial.Disconnect();
                Log("warn", $"Disconnected: selected adapter removed ({previousSelection.DisplayLabel}).");
            }

            SelectedSerialPort = null;
        }
        else if (matchedSelection is not null)
        {
            SelectedSerialPort = matchedSelection;
        }

        if (SelectedSerialPort is null && AvailableSerialPorts.Count > 0)
        {
            var connectablePort = AvailableSerialPorts.FirstOrDefault(static p => p.SupportsSerialConnection);
            SelectedSerialPort = connectablePort ?? AvailableSerialPorts[0];
        }

        if (!WfbSerialConnected)
        {
            var compatibleCount = 0;
            foreach (var item in AvailableSerialPorts)
            {
                if (item.IsRtl8812AuCompatible)
                {
                    compatibleCount++;
                }
            }

            var refreshSummary = AvailableSerialPorts.Count == 0
                ? "No serial ports detected."
                : $"Detected {AvailableSerialPorts.Count} serial port(s), RTL8812AU-compatible: {compatibleCount}.";

            if (!string.Equals(_lastSerialRefreshSummary, refreshSummary, StringComparison.Ordinal))
            {
                _lastSerialRefreshSummary = refreshSummary;
                WfbSerialStatus = refreshSummary;

                Log(
                    "debug",
                    AvailableSerialPorts.Count == 0
                        ? "No serial adapters currently detected."
                        : $"Serial refresh: {AvailableSerialPorts.Count} adapter(s), compatible={compatibleCount}, scan={stopwatch.ElapsedMilliseconds} ms.");
            }
        }

        if (initialLoad)
        {
            Log("debug", $"Initial adapter scan completed in {stopwatch.ElapsedMilliseconds} ms.");
        }

        ConnectWfbSerialCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedSerialAddressText));
    }

    [RelayCommand(CanExecute = nameof(CanConnectWfbSerial))]
    private async Task ConnectWfbSerialAsync()
    {
        if (SelectedSerialPort is null)
        {
            WfbSerialStatus = "Please select a serial port.";
            Log("warn", "Connect skipped: no adapter selected.");
            return;
        }

        var serialPortName = ResolvePortNameForConnection(SelectedSerialPort);
        if (string.IsNullOrWhiteSpace(serialPortName))
        {
            WfbSerialStatus = "Selected adapter is detected by VID:PID, but no COM port transport is exposed.";
            Log("warn", $"Connect blocked: {SelectedSerialPort.DisplayLabel} has no COM transport.");
            return;
        }

        Log("info", $"Connecting serial: {serialPortName} ({SelectedSerialPort.VidPid}).");
        var success = await _wfbSerial.ConnectAsync(serialPortName);
        if (!success)
        {
            WfbSerialStatus = "Failed to connect WFB serial port.";
            Log("error", $"Serial connect failed: {serialPortName}.");
        }
        else if (!SelectedSerialPort.IsRtl8812AuCompatible)
        {
            WfbSerialStatus = "Connected, but VID:PID is not in fpv4win RTL8812AU preferred list.";
            Log("warn", $"Connected to non-preferred VID:PID {SelectedSerialPort.VidPid}.");
        }
        else
        {
            Log("info", "Serial connection established.");
        }
    }

    private bool CanConnectWfbSerial()
        => !IsBusy
           && !WfbSerialConnected
           && SelectedSerialPort is not null;

    [RelayCommand(CanExecute = nameof(CanDisconnectWfbSerial))]
    private void DisconnectWfbSerial()
    {
        Log("info", "Disconnecting serial adapter.");
        _wfbSerial.Disconnect();
    }

    private bool CanDisconnectWfbSerial() => !IsBusy && WfbSerialConnected;

    [RelayCommand(CanExecute = nameof(CanSendWfbCommand))]
    private async Task ApplyChannelAsync()
    {
        if (!TryGetChannel(out var channel) || !TryGetChannelWidthIndex(out var channelWidthIndex))
        {
            return;
        }

        var hasControlTransport = await EnsureWfbControlTransportAsync("apply channel");
        if (!hasControlTransport)
        {
            WfbSerialStatus = "Channel settings staged. START will apply them via native pipeline or available control transport.";
            Log("info", "Apply channel staged locally without active control transport.");
            return;
        }

        try
        {
            Log(
                "debug",
                $"Apply channel: ch={channel}, width={SelectedChannelWidth}, codec={SelectedCodec}, key={WfbKeyPath}.");
            await _wfbSerial.SendLineAsync($"SET_CHANNEL {channel} {SelectedChannelWidth}");
            await _wfbSerial.SendLineAsync($"SET_CHANNEL_INDEX {channelWidthIndex}");
            await _wfbSerial.SendLineAsync($"SET_CODEC {SelectedCodec}");
            await _wfbSerial.SendLineAsync($"SET_KEY {WfbKeyPath}");
            WfbSerialStatus = "Channel configuration sent over serial.";
            Log("info", "Channel configuration sent.");
        }
        catch (Exception ex)
        {
            WfbSerialStatus = $"Failed to send channel config: {ex.Message}";
            Log("error", $"Channel configuration failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendWfbCommand))]
    private async Task StartWfbSessionAsync()
    {
        if (WfbSessionStarted)
        {
            await StopWfbSessionAsync();
            return;
        }

        if (!TryGetChannel(out var channel))
        {
            return;
        }

        if (!TryGetChannelWidthIndex(out var channelWidthIndex))
        {
            return;
        }

        var hasLocalWfbKey = TryResolveLocalWfbKeyPath(out var resolvedLocalWfbKeyPath, out var keyPathStatus);
        if (!hasLocalWfbKey)
        {
            Log("warn", keyPathStatus);
        }

        var selectedVidPid = ResolveVidPidForNativeStart();
        string? nativeFailureStatus = null;
        if (!string.IsNullOrWhiteSpace(selectedVidPid))
        {
            if (!hasLocalWfbKey)
            {
                Log("warn", "Native start skipped because local WFB key file was not found.");
            }
            else
            {
                var nativeRequest = new NativeWfbStartRequest(
                    selectedVidPid,
                    channel,
                    channelWidthIndex,
                    resolvedLocalWfbKeyPath,
                    SelectedCodec,
                    52356);

                if (_nativeWfbBridge.TryStart(nativeRequest, out var nativeStatus))
                {
                    StreamUrl = "udp://@:52356";
                    StartNativeStatusLoop();

                    WfbSerialStatus = nativeStatus;
                    WfbSessionStarted = true;
                    Log("info", nativeStatus);
                    return;
                }

                nativeFailureStatus = BuildNativeStartFailureStatus(selectedVidPid, nativeStatus);
                WfbSerialStatus = nativeFailureStatus;
                Log("warn", nativeFailureStatus);
            }
        }

        var hasControlTransport = await EnsureWfbControlTransportAsync("start session");
        if (!hasControlTransport)
        {
            WfbSerialStatus = !string.IsNullOrWhiteSpace(nativeFailureStatus)
                ? nativeFailureStatus
                : hasLocalWfbKey
                    ? "Unable to start: native bridge unavailable and no serial control transport found."
                    : $"{keyPathStatus} Also no serial control transport found.";
            Log("error", $"Start failed: {WfbSerialStatus}");

            return;
        }

        try
        {
            Log(
                "debug",
                $"Start session: ch={channel}, width={SelectedChannelWidth}, codec={SelectedCodec}, key={WfbKeyPath}.");
            await _wfbSerial.SendLineAsync($"START {channel} {SelectedChannelWidth} {SelectedCodec} {WfbKeyPath}");
            await _wfbSerial.SendLineAsync("START");

            if (string.IsNullOrWhiteSpace(StreamUrl)
                || StreamUrl.Contains("192.168.1.1", StringComparison.OrdinalIgnoreCase)
                || StreamUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
            {
                StreamUrl = "udp://@:52356";
            }

            WfbSerialStatus = "WFB start command sent over serial.";
            WfbSessionStarted = true;
            Log("info", "WFB start command sent.");
        }
        catch (Exception ex)
        {
            WfbSerialStatus = $"Failed to start WFB session: {ex.Message}";
            WfbSessionStarted = false;
            Log("error", $"WFB start failed: {ex.Message}");
        }
    }

    private bool CanSendWfbCommand() => true;

    [RelayCommand]
    private void ApplyPreset(string? presetUrl)
    {
        if (string.IsNullOrWhiteSpace(presetUrl))
        {
            return;
        }

        StreamUrl = presetUrl.Trim();
        StatusMessage = "Preset applied.";
        Log("debug", $"Stream preset applied: {StreamUrl}");
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect()
    {
        var candidateUrl = StreamUrl?.Trim();
        if (string.IsNullOrWhiteSpace(candidateUrl))
        {
            StatusMessage = "Please provide a stream URL.";
            return;
        }

        if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out var streamUri))
        {
            StatusMessage = "Invalid stream URL format.";
            Log("error", "Playback connect failed: invalid URL format.");
            return;
        }

        if (!SupportedSchemes.Contains(streamUri.Scheme))
        {
            StatusMessage = "Unsupported scheme. Use rtsp/http/https/udp/rtp.";
            Log("error", $"Playback connect failed: unsupported scheme {streamUri.Scheme}.");
            return;
        }

        IsBusy = true;
        StatusMessage = $"Connecting to {streamUri.Host}...";
        Log("info", $"Starting playback from {candidateUrl}.");

        try
        {
            if (TryStartLowLatencyFramePlayback(streamUri))
            {
                return;
            }

            StopLowLatencyPlayback();
            EnsureMediaPlayerInitialized();

            if (MediaPlayer is null)
            {
                StatusMessage = "Player is unavailable.";
                return;
            }

            using var media = new Media(_libVlc!, streamUri);
            ApplyPlaybackLatencyOptions(media, streamUri);

            if (UseRtspTcp && streamUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                media.AddOption(":rtsp-tcp");
            }

            if (!MediaPlayer.Play(media))
            {
                IsConnected = false;
                StatusMessage = "Unable to start stream playback.";
                Log("error", "Media player rejected playback start.");
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = $"Connection failed: {ex.Message}";
            Log("error", $"Playback connect failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        var stoppedLowLatency = StopLowLatencyPlayback();

        if (MediaPlayer?.IsPlaying == true)
        {
            MediaPlayer.Stop();
        }

        if (!stoppedLowLatency && MediaPlayer is null)
        {
            return;
        }

        IsConnected = false;
        StatusMessage = "Disconnected.";
        Log("info", "Playback disconnected.");
    }

    private bool CanDisconnect() => !IsBusy && (IsConnected || MediaPlayer?.IsPlaying == true || IsLowLatencyPlaybackActive);

    partial void OnIsBusyChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        ConnectWfbSerialCommand.NotifyCanExecuteChanged();
        DisconnectWfbSerialCommand.NotifyCanExecuteChanged();
        ApplyChannelCommand.NotifyCanExecuteChanged();
        StartWfbSessionCommand.NotifyCanExecuteChanged();
    }

    partial void OnWfbSerialConnectedChanged(bool value)
    {
        ConnectWfbSerialCommand.NotifyCanExecuteChanged();
        DisconnectWfbSerialCommand.NotifyCanExecuteChanged();
        ApplyChannelCommand.NotifyCanExecuteChanged();
        StartWfbSessionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSerialPortChanged(WfbSerialPortInfo? value)
    {
        ConnectWfbSerialCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedSerialAddressText));
    }

    partial void OnWfbSessionStartedChanged(bool value)
    {
        OnPropertyChanged(nameof(WfbStartButtonText));
    }

    partial void OnIsLowLatencyPlaybackActiveChanged(bool value)
    {
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnRtpPacketCountChanged(long value) => OnPropertyChanged(nameof(PacketCountersText));

    partial void OnWfbFrameCountChanged(long value) => OnPropertyChanged(nameof(PacketCountersText));

    partial void OnWifiFrameCountChanged(long value) => OnPropertyChanged(nameof(PacketCountersText));

    private void PostStatus(string message, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            StatusMessage = message;
        });
    }

    private bool TryGetChannel(out int channel)
    {
        if (int.TryParse(SelectedChannel, out channel))
        {
            return true;
        }

        WfbSerialStatus = "Invalid channel value.";
        return false;
    }

    private bool TryGetChannelWidthIndex(out int channelWidthIndex)
    {
        channelWidthIndex = ChannelWidthOptions.IndexOf(SelectedChannelWidth);
        if (channelWidthIndex >= 0)
        {
            return true;
        }

        WfbSerialStatus = "Invalid channel width value.";
        return false;
    }

    private void HandleWfbSerialLine(string message)
    {
        WfbSerialLastMessage = message;
        Log("debug", message);

        if (TryHandleJsonLine(message))
        {
            return;
        }

        var triplet = CounterTripletPattern.Match(message);
        if (triplet.Success)
        {
            if (long.TryParse(triplet.Groups["rtp"].Value, out var rtpCount)) RtpPacketCount = rtpCount;
            if (long.TryParse(triplet.Groups["wfb"].Value, out var wfbCount)) WfbFrameCount = wfbCount;
            if (long.TryParse(triplet.Groups["wifi"].Value, out var wifiCount)) WifiFrameCount = wifiCount;
        }

        foreach (Match match in SingleCounterPattern.Matches(message))
        {
            if (!long.TryParse(match.Groups["value"].Value, out var value))
            {
                continue;
            }

            var key = match.Groups["key"].Value.ToLowerInvariant();
            switch (key)
            {
                case "rtp":
                    RtpPacketCount = value;
                    break;
                case "wfb":
                    WfbFrameCount = value;
                    break;
                case "wifi":
                    WifiFrameCount = value;
                    break;
            }
        }

        var portMatch = PortPattern.Match(message);
        if (portMatch.Success && int.TryParse(portMatch.Groups["port"].Value, out var port) && port > 0)
        {
            StreamUrl = $"udp://@:{port}";
        }
    }

    private bool TryHandleJsonLine(string message)
    {
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;

            if (root.TryGetProperty("streamUrl", out var streamUrlElement)
                && streamUrlElement.ValueKind == JsonValueKind.String)
            {
                var url = streamUrlElement.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    StreamUrl = url;
                }
            }

            if (root.TryGetProperty("playerPort", out var playerPortElement)
                && playerPortElement.TryGetInt32(out var playerPort)
                && playerPort > 0)
            {
                StreamUrl = $"udp://@:{playerPort}";
            }

            if (root.TryGetProperty("rtpPktCount", out var rtpElement) && rtpElement.TryGetInt64(out var rtpCount))
            {
                RtpPacketCount = rtpCount;
            }

            if (root.TryGetProperty("wfbFrameCount", out var wfbElement) && wfbElement.TryGetInt64(out var wfbCount))
            {
                WfbFrameCount = wfbCount;
            }

            if (root.TryGetProperty("wifiFrameCount", out var wifiElement) && wifiElement.TryGetInt64(out var wifiCount))
            {
                WifiFrameCount = wifiCount;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _deviceRefreshLoopCts.Cancel();
        }
        catch
        {
            // Ignore cancellation-time errors.
        }

        StopNativeStatusLoop();
    StopLowLatencyPlayback();
        _deviceRefreshLoopCts.Dispose();

        try
        {
            if (MediaPlayer?.IsPlaying == true)
            {
                MediaPlayer.Stop();
            }
        }
        catch
        {
            // Ignore disposal-time playback errors.
        }

        if (WfbSessionStarted)
        {
            _nativeWfbBridge.TryStop(out _);
        }

        MediaPlayer?.Dispose();
        _wfbSerial.Dispose();
        _libVlc?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunDeviceRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_disposed || WfbSessionStarted || IsLowLatencyPlaybackActive)
                {
                    continue;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed)
                    {
                        _ = RefreshSerialPortsCoreAsync();
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Normal dispose path.
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => Log("warn", $"Serial auto-refresh stopped: {ex.Message}"));
        }
    }

    private void Log(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalizedLevel = NormalizeLevel(level);
        DriverLogs.Add(new GroundLogEntry(normalizedLevel, message.Trim()));

        while (DriverLogs.Count > MaxLogEntries)
        {
            DriverLogs.RemoveAt(0);
        }

        if (_consoleLoggingEnabled)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{normalizedLevel}] {message.Trim()}");

            if (!string.IsNullOrWhiteSpace(_automationLogPath))
            {
                try
                {
                    File.AppendAllText(
                        _automationLogPath,
                        $"[{DateTime.Now:HH:mm:ss}] [{normalizedLevel}] {message.Trim()}{Environment.NewLine}");
                }
                catch
                {
                    // Ignore temp log write failures.
                }
            }
        }
    }

    private static string NormalizeLevel(string level)
    {
        var normalized = (level ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "error" => "error",
            "warn" => "warn",
            "debug" => "debug",
            _ => "info"
        };
    }

    private static string InferLogLevel(string status)
    {
        if (status.Contains("error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        if (status.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
            || status.Contains("no COM", StringComparison.OrdinalIgnoreCase)
            || status.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "warn";
        }

        if (status.Contains("detected", StringComparison.OrdinalIgnoreCase)
            || status.Contains("refresh", StringComparison.OrdinalIgnoreCase))
        {
            return "debug";
        }

        return "info";
    }

    private bool ContainsPort(string portName)
    {
        foreach (var item in AvailableSerialPorts)
        {
            if (item.PortName.Equals(portName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? CreateSelectionKey(WfbSerialPortInfo? port)
    {
        if (port is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(port.SerialPortName))
        {
            return $"SERIAL:{port.SerialPortName.ToUpperInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(port.Address) && port.Address != "-")
        {
            return $"ADDR:{port.Address.ToUpperInvariant()}";
        }

        return $"VIDPID:{port.VidPid.ToUpperInvariant()}";
    }

    private WfbSerialPortInfo? FindPortByKey(string selectionKey)
    {
        foreach (var port in AvailableSerialPorts)
        {
            if (string.Equals(CreateSelectionKey(port), selectionKey, StringComparison.Ordinal))
            {
                return port;
            }
        }

        return null;
    }

    private string? ResolvePortNameForConnection(WfbSerialPortInfo selectedPort)
    {
        if (selectedPort.SupportsSerialConnection && !string.IsNullOrWhiteSpace(selectedPort.SerialPortName))
        {
            return selectedPort.SerialPortName;
        }

        var candidates = AvailableSerialPorts
            .Where(static item => item.SupportsSerialConnection && !string.IsNullOrWhiteSpace(item.SerialPortName))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        WfbSerialPortInfo? chosen = null;

        if (!string.IsNullOrWhiteSpace(selectedPort.Address) && selectedPort.Address != "-")
        {
            chosen = candidates.FirstOrDefault(item =>
                item.Address.Equals(selectedPort.Address, StringComparison.OrdinalIgnoreCase));
        }

        if (chosen is null)
        {
            var vidPidMatches = candidates
                .Where(item => item.VidPid.Equals(selectedPort.VidPid, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (vidPidMatches.Length == 1)
            {
                chosen = vidPidMatches[0];
            }
            else if (vidPidMatches.Length > 1)
            {
                WfbSerialStatus = "Multiple COM ports match selected adapter. Select the COM row directly.";
                Log("warn", $"Connect blocked: multiple COM matches for {selectedPort.VidPid}.");
                return null;
            }
        }

        if (chosen is null && selectedPort.IsRtl8812AuCompatible)
        {
            var compatibleConnectable = candidates.Where(static item => item.IsRtl8812AuCompatible).ToArray();
            if (compatibleConnectable.Length == 1)
            {
                chosen = compatibleConnectable[0];
            }
            else if (compatibleConnectable.Length == 0 && candidates.Length == 1)
            {
                chosen = candidates[0];
            }
        }

        if (chosen is null)
        {
            return null;
        }

        if (!ReferenceEquals(SelectedSerialPort, chosen))
        {
            SelectedSerialPort = chosen;
            Log("info", $"Mapped selected adapter to COM transport {chosen.SerialPortName}.");
        }

        return chosen.SerialPortName;
    }

    private async Task<bool> EnsureWfbControlTransportAsync(string operation)
    {
        if (WfbSerialConnected)
        {
            return true;
        }

        if (SelectedSerialPort is null)
        {
            WfbSerialStatus = $"Select WFB adapter before {operation}.";
            Log("warn", $"{operation} blocked: no adapter selected.");
            return false;
        }

        var serialPortName = ResolvePortNameForConnection(SelectedSerialPort);
        if (string.IsNullOrWhiteSpace(serialPortName))
        {
            return false;
        }

        Log("info", $"Auto-connecting control transport {serialPortName} for {operation}.");
        var success = await _wfbSerial.ConnectAsync(serialPortName);
        if (!success)
        {
            WfbSerialStatus = $"Failed to open control transport {serialPortName}.";
            Log("error", $"Auto-connect failed for {operation} on {serialPortName}.");
            return false;
        }

        return true;
    }

    private async Task StopWfbSessionAsync()
    {
        if (_nativeWfbBridge.IsAvailable && _nativeWfbBridge.TryStop(out var nativeStopStatus))
        {
            StopNativeStatusLoop();
            WfbSessionStarted = false;
            WfbSerialStatus = nativeStopStatus;
            Log("info", nativeStopStatus);
            return;
        }

        if (WfbSerialConnected)
        {
            try
            {
                await _wfbSerial.SendLineAsync("STOP");
                _wfbSerial.Disconnect();
                WfbSessionStarted = false;
                WfbSerialStatus = "WFB session stopped.";
                Log("info", "WFB session stopped via serial control transport.");
                return;
            }
            catch (Exception ex)
            {
                WfbSerialStatus = $"Failed to stop WFB session: {ex.Message}";
                Log("error", $"Stop failed via serial control transport: {ex.Message}");
                return;
            }
        }

        WfbSessionStarted = false;
        StopNativeStatusLoop();
        WfbSerialStatus = "No active WFB native/serial session to stop.";
        Log("warn", "STOP requested but no active native or serial session was detected.");
    }

    private void StartNativeStatusLoop()
    {
        StopNativeStatusLoop();

        ResetNativePacketLogState();
        _lastNativePlaybackUrl = null;
        _nativeStatusLoopCts = CancellationTokenSource.CreateLinkedTokenSource(_deviceRefreshLoopCts.Token);
        _nativeStatusLoopTask = Task.Run(() => PollNativeStatusLoopAsync(_nativeStatusLoopCts.Token));

        var selectedVidPid = ResolveVidPidForNativeStart() ?? "AUTO";
        Log(
            "info",
            $"Native listener armed for {selectedVidPid} on ch={SelectedChannel}, width={SelectedChannelWidth}. Launch the air side now; waiting for packets.");
    }

    private void StopNativeStatusLoop()
    {
        ResetNativePacketLogState();
        _lastNativePlaybackUrl = null;

        if (_nativeStatusLoopCts is null)
        {
            return;
        }

        try
        {
            _nativeStatusLoopCts.Cancel();
        }
        catch
        {
            // Ignore cancellation-time errors.
        }

        _nativeStatusLoopCts.Dispose();
        _nativeStatusLoopCts = null;
        _nativeStatusLoopTask = null;
    }

    private void ResetNativePacketLogState()
    {
        _lastNativeDiagnostic = null;
        _nativeStatusPollFailureCount = 0;
        _nativeStatusPollConnectedLogged = false;
        _nativeWifiSeenLogged = false;
        _nativeWfbSeenLogged = false;
        _nativeMatchedLogged = false;
        _nativeSessionKeySeenLogged = false;
        _nativeSessionReadyLogged = false;
        _nativeRtpFlowLogged = false;
    }

    private async Task PollNativeStatusLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            long nextUiStatusUpdateTick = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!_nativeWfbBridge.TryGetRuntimeStatus(out var status) || status is null)
                {
                    _nativeStatusPollFailureCount++;
                    if (_nativeStatusPollFailureCount == 10)
                    {
                        var detail = _nativeWfbBridge.LastRuntimeStatusError;
                        var message = string.IsNullOrWhiteSpace(detail)
                            ? "Native status polling is not returning packet counters yet; panel milestones may be delayed."
                            : $"Native status polling is not returning packet counters yet: {detail}";

                        await Dispatcher.UIThread.InvokeAsync(
                            () => Log("warn", message),
                            DispatcherPriority.Background);
                    }

                    continue;
                }

                _nativeStatusPollFailureCount = 0;

                if (IsLowLatencyPlaybackActive)
                {
                    var now = Environment.TickCount64;
                    if (now < nextUiStatusUpdateTick)
                    {
                        continue;
                    }

                    nextUiStatusUpdateTick = now + 1000;
                }
                else
                {
                    nextUiStatusUpdateTick = 0;
                }

                await Dispatcher.UIThread.InvokeAsync(() => ApplyNativeRuntimeStatus(status), DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop path.
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Log("warn", $"Native status polling stopped: {ex.Message}"));
        }
    }

    private void ApplyNativeRuntimeStatus(NativeWfbRuntimeStatus status)
    {
        if (!_nativeStatusPollConnectedLogged)
        {
            _nativeStatusPollConnectedLogged = true;
            Log("debug", "Native status polling connected; packet counters are live.");
        }

        RtpPacketCount = status.RtpPktCount;
        WfbFrameCount = status.WfbFrameCount;
        WifiFrameCount = status.WifiFrameCount;

        if (status.WifiFrameCount > 0 && !_nativeWifiSeenLogged)
        {
            _nativeWifiSeenLogged = true;
            Log("info", $"Native listener detected WiFi packets: wifi={status.WifiFrameCount}.");
        }

        if (status.WfbFrameCount > 0 && !_nativeWfbSeenLogged)
        {
            _nativeWfbSeenLogged = true;
            Log("info", $"Detected valid WFB frames: wfb={status.WfbFrameCount}, wifi={status.WifiFrameCount}.");
        }

        if (status.MatchedFrameCount > 0 && !_nativeMatchedLogged)
        {
            _nativeMatchedLogged = true;
            Log(
                "info",
                $"Matched target video channel: matched={status.MatchedFrameCount}, wfb={status.WfbFrameCount}, wifi={status.WifiFrameCount}.");
        }

        if (status.MatchedSessionKeyPacketCount > 0 && !_nativeSessionKeySeenLogged)
        {
            _nativeSessionKeySeenLogged = true;
            Log(
                "info",
                $"Observed session-key packets: key={status.MatchedSessionKeyPacketCount}, data={status.MatchedDataPacketCount}, matched={status.MatchedFrameCount}.");
        }

        if (status.MatchedFrameCount > 0 && status.RtpPktCount == 0 && status.DecryptErrorCount > 0)
        {
            var diagnostic = status.MatchedSessionKeyPacketCount == 0
                ? "Matched WFB data packets are arriving, but no session-key packets have been observed yet. Start the air side after the listener is armed or verify the transmitter is emitting WFB key frames."
                : "Encrypted WFB frames are arriving and session-key packets were observed, but session decryption is still failing. Re-check channel/radio parity against fpv4win and confirm the active keypair matches the live air unit.";
            if (!string.Equals(_lastNativeDiagnostic, diagnostic, StringComparison.Ordinal))
            {
                _lastNativeDiagnostic = diagnostic;
                Log("warn", diagnostic);
            }
        }
        else if (status.SessionReady && status.DecodedPacketCount > 0)
        {
            _lastNativeDiagnostic = null;
        }

        if (status.SessionReady && !_nativeSessionReadyLogged)
        {
            _nativeSessionReadyLogged = true;
            Log(
                "info",
                $"WFB session is ready: decoded={status.DecodedPacketCount}, decryptErr={status.DecryptErrorCount}, bad={status.BadPacketCount}.");
        }

        if (status.RtpPktCount > 0 && !_nativeRtpFlowLogged)
        {
            _nativeRtpFlowLogged = true;
            Log(
                "info",
                $"RTP packets are flowing: rtp={status.RtpPktCount}, decoded={status.DecodedPacketCount}, codec={status.Codec}.");
        }

        if (!status.StreamReady || status.PayloadType < 0 || string.IsNullOrWhiteSpace(status.Codec))
        {
            return;
        }

        var sdpUrl = BuildNativePlayerSdpUrl(status.PlayerPort, status.PayloadType, status.Codec);
        if (string.IsNullOrWhiteSpace(sdpUrl)
            || string.Equals(_lastNativePlaybackUrl, sdpUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastNativePlaybackUrl = sdpUrl;
        StreamUrl = sdpUrl;
        Log("info", $"Native bridge reported RTP stream: codec={status.Codec}, pt={status.PayloadType}, port={status.PlayerPort}.");

        if (MediaPlayer?.IsPlaying != true)
        {
            Connect();
        }
    }

    private string? BuildNativePlayerSdpUrl(int port, int payloadType, string codec)
    {
        if (port <= 0 || payloadType < 0 || string.IsNullOrWhiteSpace(codec))
        {
            return null;
        }

        var normalizedCodec = codec.Trim().ToUpperInvariant();
        var sdpPath = Path.Combine(Path.GetTempPath(), $"fs_zb_ground_app_{port}.sdp");
        File.WriteAllText(
            sdpPath,
            string.Join(
                Environment.NewLine,
                [
                    "v=0",
                    "o=- 0 0 IN IP4 127.0.0.1",
                    "s=No Name",
                    "c=IN IP4 127.0.0.1",
                    "t=0 0",
                    $"m=video {port} RTP/AVP {payloadType}",
                    $"a=rtpmap:{payloadType} {normalizedCodec}/90000",
                    string.Empty
                ]));

        return new Uri(sdpPath).AbsoluteUri;
    }

    private string? ResolveVidPidForNativeStart()
    {
        if (SelectedSerialPort is null)
        {
            return null;
        }

        if (IsValidVidPid(SelectedSerialPort.VidPid))
        {
            return SelectedSerialPort.VidPid.ToUpperInvariant();
        }

        var compatibleCandidate = AvailableSerialPorts
            .FirstOrDefault(static item => item.IsRtl8812AuCompatible && IsValidVidPid(item.VidPid));

        return compatibleCandidate?.VidPid.ToUpperInvariant();
    }

    private static string BuildNativeStartFailureStatus(string vidPid, string nativeStatus)
    {
        if (!nativeStatus.Contains("Cannot open selected USB adapter via libusb.", StringComparison.OrdinalIgnoreCase))
        {
            return nativeStatus;
        }

        return WindowsUsbDriverInspector.TryBuildNativeAccessGuidance(vidPid, out var guidance)
            ? guidance
            : nativeStatus;
    }

    private static bool IsValidVidPid(string? vidPid)
    {
        if (string.IsNullOrWhiteSpace(vidPid))
        {
            return false;
        }

        return Regex.IsMatch(
            vidPid,
            @"^[0-9A-F]{4}:[0-9A-F]{4}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private bool TryResolveLocalWfbKeyPath(out string resolvedPath, out string status)
    {
        resolvedPath = string.Empty;

        var configuredPath = (WfbKeyPath ?? string.Empty).Trim();
        var keyFileName = string.IsNullOrWhiteSpace(configuredPath)
            ? "gs.key"
            : Path.GetFileName(configuredPath);

        var candidates = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(rawPath);
            }
            catch
            {
                return;
            }

            if (visited.Add(fullPath))
            {
                candidates.Add(fullPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (Path.IsPathRooted(configuredPath))
            {
                AddCandidate(configuredPath);
            }
            else
            {
                AddCandidate(Path.Combine(Environment.CurrentDirectory, configuredPath));
                AddCandidate(Path.Combine(Environment.CurrentDirectory, "fs_zb_serial_gnd_app_avalonia", keyFileName));
                AddCandidate(Path.Combine(AppContext.BaseDirectory, configuredPath));
            }
        }

        foreach (var ancestor in EnumerateAncestorDirectories(AppContext.BaseDirectory, 8))
        {
            AddCandidate(Path.Combine(ancestor, keyFileName));
            AddCandidate(Path.Combine(ancestor, "fs_zb_serial_gnd_app_avalonia", keyFileName));
            AddCandidate(Path.Combine(ancestor, "__references", "fpv4win-main", "fpv4win-main", keyFileName));
            AddCandidate(Path.Combine(ancestor, "__references", "fpv4win-main", "fpv4win-main", "gs.key"));
        }

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            resolvedPath = candidate;
            status = $"Resolved WFB key path: {resolvedPath}";
            return true;
        }

        var sampleCandidates = string.Join("; ", candidates.Take(3));
        status = string.IsNullOrWhiteSpace(sampleCandidates)
            ? "WFB key file not found. Set a valid path in Session Setup."
            : $"WFB key file not found for '{WfbKeyPath}'. Tried: {sampleCandidates}";

        return false;
    }

    private static IEnumerable<string> EnumerateAncestorDirectories(string startPath, int maxDepth)
    {
        var current = new DirectoryInfo(startPath);
        var depth = 0;

        while (current is not null && depth <= maxDepth)
        {
            yield return current.FullName;
            current = current.Parent;
            depth++;
        }
    }
}

public sealed class GroundLogEntry
{
    public GroundLogEntry(string level, string message)
    {
        Timestamp = DateTime.Now;
        Level = level;
        Message = message;
    }

    public DateTime Timestamp { get; }

    public string Level { get; }

    public string Message { get; }

    public string DisplayText => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";

    public string LevelColor => Level switch
    {
        "error" => "#ff0000",
        "info" => "#0f7340",
        "warn" => "#e8c538",
        "debug" => "#3296de",
        _ => "#EAF2FF"
    };
}
