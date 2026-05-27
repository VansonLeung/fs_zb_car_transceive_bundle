using System;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FsZbGroundApp.Services;

public sealed record NativeWfbStartRequest(
    string VidPid,
    int Channel,
    int ChannelWidthIndex,
    string KeyPath,
    string Codec,
    int PlayerPort);

public sealed class NativeWfbRuntimeStatus
{
    public bool Running { get; init; }

    public int PlayerPort { get; init; }

    public long WifiFrameCount { get; init; }

    public long WfbFrameCount { get; init; }

    public long MatchedFrameCount { get; init; }

    public long MatchedDataPacketCount { get; init; }

    public long MatchedSessionKeyPacketCount { get; init; }

    public long MatchedUnknownPacketCount { get; init; }

    public long RtpPktCount { get; init; }

    public bool StreamReady { get; init; }

    public bool SessionReady { get; init; }

    public long DecryptErrorCount { get; init; }

    public long DecodedPacketCount { get; init; }

    public long BadPacketCount { get; init; }

    public int PayloadType { get; init; }

    public uint Ssrc { get; init; }

    public string Codec { get; init; } = string.Empty;
}

public sealed class NativeWfbLibusbBridge
{
    private const string BridgeLibraryName = "fpv4win_bridge";

    private bool _probeAttempted;
    private bool _isAvailable;
    private string _availabilityMessage = "Native bridge not probed.";
    private string? _lastRuntimeStatusError;

    public bool IsAvailable
    {
        get
        {
            EnsureProbe();
            return _isAvailable;
        }
    }

    public string AvailabilityMessage
    {
        get
        {
            EnsureProbe();
            return _availabilityMessage;
        }
    }

    public string? LastRuntimeStatusError => _lastRuntimeStatusError;

    public bool TryStart(NativeWfbStartRequest request, out string status)
    {
        EnsureProbe();
        if (!_isAvailable)
        {
            status = $"Native libusb bridge unavailable: {_availabilityMessage}";
            return false;
        }

        try
        {
            var rc = fpv4win_bridge_start(
                request.VidPid,
                request.Channel,
                request.ChannelWidthIndex,
                request.KeyPath,
                request.Codec,
                request.PlayerPort);

            if (rc == 1)
            {
                status = $"Native libusb pipeline started for {request.VidPid}.";
                return true;
            }

            status = TryGetLastError();
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _isAvailable = false;
            _availabilityMessage = ex.Message;
            status = $"Native libusb bridge call failed: {ex.Message}";
            return false;
        }
    }

    public bool TryStop(out string status)
    {
        EnsureProbe();
        if (!_isAvailable)
        {
            status = "Native libusb bridge unavailable.";
            return false;
        }

        try
        {
            var rc = fpv4win_bridge_stop();
            if (rc == 1)
            {
                status = "Native libusb pipeline stopped.";
                return true;
            }

            status = TryGetLastError();
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _isAvailable = false;
            _availabilityMessage = ex.Message;
            status = $"Native libusb bridge stop failed: {ex.Message}";
            return false;
        }
    }

    public bool TryGetRuntimeStatus(out NativeWfbRuntimeStatus? status)
    {
        status = null;
        _lastRuntimeStatusError = null;

        EnsureProbe();
        if (!_isAvailable)
        {
            _lastRuntimeStatusError = _availabilityMessage;
            return false;
        }

        try
        {
            var ptr = fpv4win_bridge_get_status_json();
            var text = ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrWhiteSpace(text))
            {
                _lastRuntimeStatusError = "fpv4win_bridge_get_status_json returned an empty payload.";
                return false;
            }

            status = JsonSerializer.Deserialize<NativeWfbRuntimeStatus>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (status is null)
            {
                _lastRuntimeStatusError = "Native runtime status JSON deserialized to null.";
            }

            return status is not null;
        }
        catch (JsonException ex)
        {
            _lastRuntimeStatusError = $"Native runtime status JSON parse failed: {ex.Message}";
            status = null;
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _lastRuntimeStatusError = $"Native runtime status call failed: {ex.Message}";
            status = null;
            return false;
        }
        catch
        {
            _lastRuntimeStatusError = "Native runtime status polling failed with an unknown error.";
            status = null;
            return false;
        }
    }

    private void EnsureProbe()
    {
        if (_probeAttempted)
        {
            return;
        }

        _probeAttempted = true;

        try
        {
            var rc = fpv4win_bridge_probe();
            _isAvailable = rc == 1;
            _availabilityMessage = _isAvailable
                ? "fpv4win_bridge loaded."
                : "fpv4win_bridge reported unavailable.";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _isAvailable = false;
            _availabilityMessage = ex.Message;
        }
    }

    private static string TryGetLastError()
    {
        try
        {
            var ptr = fpv4win_bridge_get_last_error();
            var text = ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
            return string.IsNullOrWhiteSpace(text)
                ? "Native libusb pipeline reported an unknown error."
                : text;
        }
        catch
        {
            return "Native libusb pipeline reported an unknown error.";
        }
    }

    [DllImport(BridgeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fpv4win_bridge_probe")]
    private static extern int fpv4win_bridge_probe();

    [DllImport(BridgeLibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "fpv4win_bridge_start")]
    private static extern int fpv4win_bridge_start(
        string vidPid,
        int channel,
        int channelWidthIndex,
        string keyPath,
        string codec,
        int playerPort);

    [DllImport(BridgeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fpv4win_bridge_stop")]
    private static extern int fpv4win_bridge_stop();

    [DllImport(BridgeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fpv4win_bridge_get_last_error")]
    private static extern IntPtr fpv4win_bridge_get_last_error();

    [DllImport(BridgeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "fpv4win_bridge_get_status_json")]
    private static extern IntPtr fpv4win_bridge_get_status_json();
}
