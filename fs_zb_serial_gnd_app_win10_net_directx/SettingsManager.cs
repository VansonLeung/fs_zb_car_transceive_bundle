using System.IO;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace RCCarController
{
    public class SettingsManager
    {
        private const string SETTINGS_FILE = "settings.ini";

        public string? SavedPort { get; private set; }
        public string? SavedBaud { get; private set; }
        public bool WebSocketEnabled { get; private set; } = true;
        public bool ReverseSteeringInput { get; private set; }
        public bool ReverseThrottleInput { get; private set; }
        public bool AutoConnectSerial { get; private set; }
        public int SteeringOffset { get; private set; }
        public List<string> MacAddresses { get; } = new();

        public void LoadSettings(ComboBox baudComboBox)
        {
            try
            {
                WebSocketEnabled = true;
                ReverseSteeringInput = false;
                ReverseThrottleInput = false;
                AutoConnectSerial = false;
                SteeringOffset = 0;
                MacAddresses.Clear();

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
                                    SavedPort = parts[1];
                                    break;
                                case "Baud":
                                    SavedBaud = parts[1];
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
                                case "WebSocketEnabled":
                                    if (bool.TryParse(parts[1], out var wsEnabled))
                                    {
                                        WebSocketEnabled = wsEnabled;
                                    }
                                    break;
                                case "ReverseSteering":
                                    if (bool.TryParse(parts[1], out var reverseSteering))
                                    {
                                        ReverseSteeringInput = reverseSteering;
                                    }
                                    break;
                                case "ReverseThrottle":
                                    if (bool.TryParse(parts[1], out var reverseThrottle))
                                    {
                                        ReverseThrottleInput = reverseThrottle;
                                    }
                                    break;
                                case "AutoConnectSerial":
                                    if (bool.TryParse(parts[1], out var autoConnect))
                                    {
                                        AutoConnectSerial = autoConnect;
                                    }
                                    break;
                                case "SteeringOffset":
                                    if (int.TryParse(parts[1], out var offset))
                                    {
                                        SteeringOffset = offset;
                                    }
                                    break;
                                case "MacList":
                                    var entries = parts[1]
                                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(m => m.Trim().ToUpperInvariant())
                                        .Where(m => !string.IsNullOrWhiteSpace(m))
                                        .ToList();
                                    if (entries.Count > 0)
                                    {
                                        MacAddresses.Clear();
                                        MacAddresses.AddRange(entries);
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Handle error
            }
        }

        public void SaveSettings(
            string? port = null,
            object? baudItem = null,
            bool? websocketEnabled = null,
            bool? reverseSteering = null,
            bool? reverseThrottle = null,
            bool? autoConnectSerial = null,
            int? steeringOffset = null)
        {
            try
            {
                if (port != null)
                    SavedPort = port;

                if (baudItem != null)
                {
                    SavedBaud = (baudItem as ComboBoxItem)?.Content?.ToString() ?? baudItem.ToString();
                }

                if (websocketEnabled.HasValue)
                    WebSocketEnabled = websocketEnabled.Value;
                if (reverseSteering.HasValue)
                    ReverseSteeringInput = reverseSteering.Value;
                if (reverseThrottle.HasValue)
                    ReverseThrottleInput = reverseThrottle.Value;
                if (autoConnectSerial.HasValue)
                    AutoConnectSerial = autoConnectSerial.Value;
                if (steeringOffset.HasValue)
                    SteeringOffset = steeringOffset.Value;

                WriteSettingsFile();
            }
            catch (Exception)
            {
                // Handle error
            }
        }

        public void SaveMacList(IEnumerable<string> macs)
        {
            try
            {
                MacAddresses.Clear();
                foreach (var mac in macs)
                {
                    if (!string.IsNullOrWhiteSpace(mac))
                    {
                        MacAddresses.Add(mac.Trim().ToUpperInvariant());
                    }
                }
                WriteSettingsFile();
            }
            catch (Exception)
            {
                // Handle error
            }
        }

        private void WriteSettingsFile()
        {
            var settings = new List<string>();
            if (!string.IsNullOrEmpty(SavedPort))
                settings.Add($"Port={SavedPort}");
            if (!string.IsNullOrEmpty(SavedBaud))
                settings.Add($"Baud={SavedBaud}");
            settings.Add($"WebSocketEnabled={WebSocketEnabled}");
            settings.Add($"ReverseSteering={ReverseSteeringInput}");
            settings.Add($"ReverseThrottle={ReverseThrottleInput}");
            settings.Add($"AutoConnectSerial={AutoConnectSerial}");
            settings.Add($"SteeringOffset={SteeringOffset}");
            if (MacAddresses.Count > 0)
            {
                settings.Add($"MacList={string.Join(';', MacAddresses)}");
            }

            File.WriteAllLines(SETTINGS_FILE, settings);
        }
    }
}
