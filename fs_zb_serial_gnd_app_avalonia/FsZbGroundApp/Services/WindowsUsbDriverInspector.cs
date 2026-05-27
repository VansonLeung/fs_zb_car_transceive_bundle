using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace FsZbGroundApp.Services;

public sealed record WindowsUsbDriverBindingInfo(
    string VidPid,
    string DeviceId,
    string FriendlyName,
    string Provider,
    string DeviceClass,
    string Service,
    string DriverVersion,
    string InfName,
    string? AvailableWinUsbInfName);

public static class WindowsUsbDriverInspector
{
    private sealed record BindingCandidate(
        string DeviceId,
        string FriendlyName,
        string Provider,
        string DeviceClass,
        string Service,
        string DriverVersion,
        string InfName,
        int ConfigManagerErrorCode);

    public static bool TryBuildNativeAccessGuidance(string vidPid, out string message)
    {
        message = string.Empty;

        if (!TryGetActiveBinding(vidPid, out var binding))
        {
            return false;
        }

        if (string.Equals(binding.Service, "WinUSB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(binding.DeviceClass, "USBDevice", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var winUsbHint = string.IsNullOrWhiteSpace(binding.AvailableWinUsbInfName)
            ? "No installed WinUSB/libwdi package was detected for this VID:PID."
            : $"Detected installed WinUSB package {binding.AvailableWinUsbInfName}.";

        message =
            $"Native libusb cannot open {binding.VidPid} because Windows currently binds it to " +
            $"provider '{binding.Provider}', service '{binding.Service}', class '{binding.DeviceClass}' (INF {binding.InfName}, version {binding.DriverVersion}). " +
            $"Native capture requires a WinUSB/libwdi binding instead of the Realtek Net driver. {winUsbHint}";

        return true;
    }

    public static bool TryGetActiveBinding(string vidPid, out WindowsUsbDriverBindingInfo binding)
    {
        binding = default!;

        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(vidPid))
        {
            return false;
        }

        var normalizedVidPid = vidPid.Trim().ToUpperInvariant();
        var hardwareId = normalizedVidPid.Replace(':', '&');
        var deviceIdPrefix = $"USB\\VID_{hardwareId[..4]}&PID_{hardwareId[^4..]}";

        try
        {
            var candidates = new List<BindingCandidate>();

            using var entitySearcher = new ManagementObjectSearcher(
                $"SELECT PNPDeviceID, Name, Service, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '{deviceIdPrefix.Replace("\\", "\\\\")}%' ");

            foreach (var entity in entitySearcher.Get().OfType<ManagementObject>())
            {
                var deviceId = entity["PNPDeviceID"] as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                var friendlyName = entity["Name"] as string ?? string.Empty;
                var service = entity["Service"] as string ?? string.Empty;
                var configManagerErrorCode = entity["ConfigManagerErrorCode"] is null
                    ? -1
                    : Convert.ToInt32(entity["ConfigManagerErrorCode"]);

                using var signedDriverSearcher = new ManagementObjectSearcher(
                    $"SELECT DeviceID, DriverProviderName, DriverVersion, InfName, DeviceClass FROM Win32_PnPSignedDriver WHERE DeviceID = '{deviceId.Replace("\\", "\\\\")}'");

                var signedDriver = signedDriverSearcher.Get().OfType<ManagementObject>().FirstOrDefault();
                var provider = signedDriver?["DriverProviderName"] as string ?? string.Empty;
                var driverVersion = signedDriver?["DriverVersion"] as string ?? string.Empty;
                var infName = signedDriver?["InfName"] as string ?? string.Empty;
                var deviceClass = signedDriver?["DeviceClass"] as string ?? string.Empty;

                candidates.Add(new BindingCandidate(
                    deviceId,
                    friendlyName,
                    provider,
                    deviceClass,
                    service,
                    driverVersion,
                    infName,
                    configManagerErrorCode));
            }

            var bestCandidate = candidates
                .OrderByDescending(static candidate => candidate.ConfigManagerErrorCode == 0)
                .ThenByDescending(static candidate => string.Equals(candidate.Service, "WinUSB", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(static candidate => string.Equals(candidate.DeviceClass, "USBDevice", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(static candidate => !string.IsNullOrWhiteSpace(candidate.InfName))
                .FirstOrDefault();

            if (bestCandidate is null)
            {
                return false;
            }

            binding = new WindowsUsbDriverBindingInfo(
                normalizedVidPid,
                bestCandidate.DeviceId,
                bestCandidate.FriendlyName,
                bestCandidate.Provider,
                bestCandidate.DeviceClass,
                bestCandidate.Service,
                bestCandidate.DriverVersion,
                bestCandidate.InfName,
                TryFindInstalledWinUsbInf(bestCandidate.DeviceId, normalizedVidPid));

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryFindInstalledWinUsbInf(string deviceId, string vidPid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var pnputilMatch = TryFindInstalledWinUsbInfFromPnPUtil(deviceId);
        if (!string.IsNullOrWhiteSpace(pnputilMatch))
        {
            return pnputilMatch;
        }

        var infRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        if (!Directory.Exists(infRoot))
        {
            return null;
        }

        var hardwareId = $"USB\\VID_{vidPid[..4]}&PID_{vidPid[^4..]}";

        foreach (var infPath in Directory.EnumerateFiles(infRoot, "oem*.inf"))
        {
            try
            {
                var contents = File.ReadAllText(infPath, Encoding.Unicode);
                if (contents.Contains(hardwareId, StringComparison.OrdinalIgnoreCase)
                    && contents.Contains("WinUSB", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(infPath);
                }
            }
            catch
            {
                // Ignore unreadable INFs and continue searching.
            }
        }

        return null;
    }

    private static string? TryFindInstalledWinUsbInfFromPnPUtil(string deviceId)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pnputil",
                Arguments = $"/enum-devices /instanceid \"{deviceId}\" /drivers",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            string? currentInf = null;
            foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    currentInf = null;
                    continue;
                }

                if (line.StartsWith("Driver Name:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("驅動程式名稱", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"oem\d+\.inf", RegexOptions.IgnoreCase);
                    currentInf = match.Success ? match.Value : null;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentInf)
                    && (line.Contains("libwdi", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("WinUSB", StringComparison.OrdinalIgnoreCase)))
                {
                    return currentInf;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}