using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Management;
using Microsoft.Win32;

namespace FsZbGroundApp.Services;

public sealed class WfbSerialPortInfo
{
    public string PortName { get; init; } = string.Empty;

    public string? SerialPortName { get; init; }

    public string VidPid { get; init; } = "UNKNOWN";

    public string Address { get; init; } = "-";

    public string FriendlyName { get; init; } = string.Empty;

    public bool IsRtl8812AuCompatible { get; init; }

    public bool SupportsSerialConnection => !string.IsNullOrWhiteSpace(SerialPortName);

    public string DisplayLabel
    {
        get
        {
            var compat = IsRtl8812AuCompatible ? "RTL8812AU-COMPAT" : "GENERIC";
            return $"{PortName} | {VidPid} | {compat}";
        }
    }

    public string AddressLabel => $"{Address}";
}

public sealed record WfbUsbDeviceMetadata(string VidPid, string Address, string FriendlyName = "");

public sealed class WfbSerialClient : IDisposable
{
    // Aligned with the fpv4win executable candidate set provided by user verification.
    private static readonly HashSet<string> KnownRtl8812AuVidPid = new(StringComparer.OrdinalIgnoreCase)
    {
        "0BDA:881A",
        "2B89:0043"
    };

    private static readonly Regex VidPidPattern = new(
        @"^VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4})(?:[&\\].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UsbPnpDeviceIdPattern = new(
        @"^USB\\(?<hardware>VID_[0-9A-F]{4}&PID_[0-9A-F]{4}(?:&[^\\]+)?)\\(?<instance>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int FixedBaudRate = 115200;

    private readonly object _sync = new();
    private readonly StringBuilder _lineBuffer = new();
    private readonly Func<IReadOnlyList<string>> _portNamesProvider;
    private readonly Func<Dictionary<string, WfbUsbDeviceMetadata>> _usbMetadataProvider;
    private readonly Func<IReadOnlyList<WfbUsbDeviceMetadata>> _knownCandidateProvider;

    private SerialPort? _serialPort;

    public event Action<string>? LineReceived;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ConnectionStateChanged;

    public bool IsConnected { get; private set; }

    public WfbSerialClient()
        : this(
            static () => SerialPort.GetPortNames(),
            TryGetUsbMetadataByComPort,
            TryGetKnownCandidateUsbDevices)
    {
    }

    public WfbSerialClient(
        Func<IReadOnlyList<string>> portNamesProvider,
        Func<Dictionary<string, WfbUsbDeviceMetadata>> usbMetadataProvider,
        Func<IReadOnlyList<WfbUsbDeviceMetadata>>? knownCandidateProvider = null)
    {
        _portNamesProvider = portNamesProvider ?? throw new ArgumentNullException(nameof(portNamesProvider));
        _usbMetadataProvider = usbMetadataProvider ?? throw new ArgumentNullException(nameof(usbMetadataProvider));
        _knownCandidateProvider = knownCandidateProvider
            ?? DefaultKnownCandidateProvider;
    }

    public IReadOnlyList<WfbSerialPortInfo> GetAvailablePorts()
    {
        var ports = _portNamesProvider()
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var metadataByPort = _usbMetadataProvider();

        var results = new List<WfbSerialPortInfo>(ports.Length);
        foreach (var port in ports)
        {
            var key = port.ToUpperInvariant();
            metadataByPort.TryGetValue(key, out WfbUsbDeviceMetadata? metadata);

            var vidPid = metadata?.VidPid ?? "UNKNOWN";
            var address = metadata?.Address ?? "-";
            var friendlyName = metadata?.FriendlyName ?? string.Empty;

            results.Add(new WfbSerialPortInfo
            {
                PortName = port,
                SerialPortName = port,
                VidPid = vidPid,
                Address = address,
                FriendlyName = friendlyName,
                IsRtl8812AuCompatible = KnownRtl8812AuVidPid.Contains(vidPid)
            });
        }

        var existingAddressSet = new HashSet<string>(
            results
                .Select(static item => item.Address)
                .Where(static item => !string.IsNullOrWhiteSpace(item)),
            StringComparer.OrdinalIgnoreCase);

        var existingVidPidSet = new HashSet<string>(
            results
                .Where(static item => item.IsRtl8812AuCompatible)
                .Select(static item => item.VidPid),
            StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in _knownCandidateProvider())
        {
            if (!KnownRtl8812AuVidPid.Contains(candidate.VidPid))
            {
                continue;
            }

            if ((!string.IsNullOrWhiteSpace(candidate.Address) && existingAddressSet.Contains(candidate.Address))
                || existingVidPidSet.Contains(candidate.VidPid))
            {
                continue;
            }

            results.Add(new WfbSerialPortInfo
            {
                PortName = $"USB {candidate.VidPid}",
                SerialPortName = null,
                VidPid = candidate.VidPid,
                Address = candidate.Address,
                FriendlyName = candidate.FriendlyName,
                IsRtl8812AuCompatible = true
            });

            if (!string.IsNullOrWhiteSpace(candidate.Address))
            {
                existingAddressSet.Add(candidate.Address);
            }

            existingVidPidSet.Add(candidate.VidPid);
        }

        return results;
    }

    public Task<bool> ConnectAsync(string portName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new InvalidOperationException("Serial port is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Disconnect();

        try
        {
            var serialPort = new SerialPort(portName, FixedBaudRate)
            {
                NewLine = "\n",
                ReadTimeout = 250,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            serialPort.DataReceived += SerialPortDataReceived;
            serialPort.Open();

            lock (_sync)
            {
                _lineBuffer.Clear();
                _serialPort = serialPort;
                IsConnected = true;
            }

            StatusChanged?.Invoke($"WFB serial connected: {portName}. (fixed {FixedBaudRate})");
            ConnectionStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"WFB serial connect failed: {ex.Message}");
            ConnectionStateChanged?.Invoke(false);
            return Task.FromResult(false);
        }
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SerialPort? serialPort;
        lock (_sync)
        {
            serialPort = _serialPort;
        }

        if (!IsConnected || serialPort is null || !serialPort.IsOpen)
        {
            throw new InvalidOperationException("WFB serial is not connected.");
        }

        var payload = line ?? string.Empty;
        if (!payload.EndsWith("\n", StringComparison.Ordinal))
        {
            payload += "\n";
        }

        serialPort.Write(payload);
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        SerialPort? serialPort;

        lock (_sync)
        {
            serialPort = _serialPort;
            _serialPort = null;

            if (!IsConnected)
            {
                return;
            }

            IsConnected = false;
            _lineBuffer.Clear();
        }

        if (serialPort is not null)
        {
            try { serialPort.DataReceived -= SerialPortDataReceived; } catch { }
            try { if (serialPort.IsOpen) serialPort.Close(); } catch { }
            try { serialPort.Dispose(); } catch { }
        }

        StatusChanged?.Invoke("WFB serial disconnected.");
        ConnectionStateChanged?.Invoke(false);
    }

    private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (sender is not SerialPort serialPort)
        {
            return;
        }

        string chunk;
        try
        {
            chunk = serialPort.ReadExisting();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"WFB serial read error: {ex.Message}");
            return;
        }

        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        lock (_sync)
        {
            _lineBuffer.Append(chunk);

            while (true)
            {
                var snapshot = _lineBuffer.ToString();
                var newlineIndex = snapshot.IndexOf('\n');
                if (newlineIndex < 0)
                {
                    break;
                }

                var line = snapshot[..newlineIndex].Trim('\r', '\n', ' ', '\t');
                _lineBuffer.Remove(0, newlineIndex + 1);

                if (!string.IsNullOrWhiteSpace(line))
                {
                    LineReceived?.Invoke(line);
                }
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
    }

    private static Dictionary<string, WfbUsbDeviceMetadata> TryGetUsbMetadataByComPort()
    {
        var result = new Dictionary<string, WfbUsbDeviceMetadata>(StringComparer.OrdinalIgnoreCase);

        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        var hasLiveUsbSnapshot = TryGetConnectedUsbDevices(out var connectedUsbDevices);
        var connectedByInstance = hasLiveUsbSnapshot
            ? connectedUsbDevices
                .Select(static item => (item, instanceId: TryExtractInstanceId(item.Address)))
                .Where(static item => !string.IsNullOrWhiteSpace(item.instanceId))
                .GroupBy(static item => item.instanceId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static g => g.Key,
                    static g => g.First().item,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WfbUsbDeviceMetadata>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbRoot is null)
            {
                return result;
            }

            foreach (var vidPidKeyName in usbRoot.GetSubKeyNames())
            {
                var match = VidPidPattern.Match(vidPidKeyName);
                if (!match.Success)
                {
                    continue;
                }

                var vidPid = $"{match.Groups["vid"].Value.ToUpperInvariant()}:{match.Groups["pid"].Value.ToUpperInvariant()}";

                using var vidPidKey = usbRoot.OpenSubKey(vidPidKeyName);
                if (vidPidKey is null)
                {
                    continue;
                }

                foreach (var instanceId in vidPidKey.GetSubKeyNames())
                {
                    if (hasLiveUsbSnapshot && !connectedByInstance.ContainsKey(instanceId))
                    {
                        continue;
                    }

                    using var instanceKey = vidPidKey.OpenSubKey(instanceId);
                    using var deviceParametersKey = instanceKey?.OpenSubKey("Device Parameters");

                    var comPort = deviceParametersKey?.GetValue("PortName") as string;
                    if (string.IsNullOrWhiteSpace(comPort))
                    {
                        continue;
                    }

                    connectedByInstance.TryGetValue(instanceId, out var liveMetadata);

                    var friendlyName = liveMetadata?.FriendlyName
                        ?? instanceKey?.GetValue("FriendlyName") as string
                        ?? instanceKey?.GetValue("DeviceDesc") as string
                        ?? string.Empty;

                    var address = liveMetadata?.Address ?? $"USB\\{vidPidKeyName}\\{instanceId}";
                    var normalizedVidPid = liveMetadata?.VidPid ?? vidPid;

                    result[comPort.ToUpperInvariant()] = new WfbUsbDeviceMetadata(
                        normalizedVidPid,
                        address,
                        friendlyName);
                }
            }
        }
        catch
        {
            // Keep serial listing functional even if USB metadata lookup fails.
        }

        return result;
    }

    private static IReadOnlyList<WfbUsbDeviceMetadata> TryGetKnownCandidateUsbDevices()
    {
        if (TryGetConnectedUsbDevices(out var liveUsbDevices))
        {
            return liveUsbDevices
                .Where(static item => KnownRtl8812AuVidPid.Contains(item.VidPid))
                .GroupBy(static item => item.Address, StringComparer.OrdinalIgnoreCase)
                .Select(static g => g.First())
                .ToArray();
        }

        return Array.Empty<WfbUsbDeviceMetadata>();
    }

    private IReadOnlyList<WfbUsbDeviceMetadata> DefaultKnownCandidateProvider()
    {
        var metadata = _usbMetadataProvider();
        return metadata
            .Values
            .Where(static item => KnownRtl8812AuVidPid.Contains(item.VidPid))
            .GroupBy(static item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Select(static g => g.First())
            .ToArray();
    }

    private static bool TryGetConnectedUsbDevices(out IReadOnlyList<WfbUsbDeviceMetadata> devices)
    {
        devices = Array.Empty<WfbUsbDeviceMetadata>();

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var seenAddress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var liveDevices = new List<WfbUsbDeviceMetadata>();

            using var searcher = new ManagementObjectSearcher(
                @"SELECT PNPDeviceID, Name, Caption, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB\\VID_%&PID_%' AND ConfigManagerErrorCode = 0");

            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                var pnpDeviceId = obj["PNPDeviceID"] as string;
                if (!TryParseUsbPnpDevice(pnpDeviceId, out var vidPid, out var address))
                {
                    continue;
                }

                if (!seenAddress.Add(address))
                {
                    continue;
                }

                var friendlyName = (obj["Name"] as string)
                    ?? (obj["Caption"] as string)
                    ?? string.Empty;

                liveDevices.Add(new WfbUsbDeviceMetadata(vidPid, address, friendlyName));
            }

            devices = liveDevices;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseUsbPnpDevice(string? pnpDeviceId, out string vidPid, out string address)
    {
        vidPid = string.Empty;
        address = string.Empty;

        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return false;
        }

        var match = UsbPnpDeviceIdPattern.Match(pnpDeviceId.Trim());
        if (!match.Success)
        {
            return false;
        }

        var hardwareId = match.Groups["hardware"].Value;
        var instanceId = match.Groups["instance"].Value;

        var vidPidMatch = VidPidPattern.Match(hardwareId);
        if (!vidPidMatch.Success)
        {
            return false;
        }

        var vid = vidPidMatch.Groups["vid"].Value.ToUpperInvariant();
        var pid = vidPidMatch.Groups["pid"].Value.ToUpperInvariant();

        vidPid = $"{vid}:{pid}";
        address = $"USB\\{hardwareId.ToUpperInvariant()}\\{instanceId}";
        return true;
    }

    private static string? TryExtractInstanceId(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var separatorIndex = address.LastIndexOf('\\');
        if (separatorIndex < 0 || separatorIndex >= address.Length - 1)
        {
            return null;
        }

        return address[(separatorIndex + 1)..];
    }
}