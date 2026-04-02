using System.Collections.Generic;
using System.IO;
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
        public int SteeringStepLimit { get; private set; }
        public int ThrottleStepLimit { get; private set; }
        public List<ControlMappingRange> ThrottleMappings { get; private set; } = new List<ControlMappingRange>();
        public List<ControlMappingRange> SteeringMappings { get; private set; } = new List<ControlMappingRange>();
        public int StartIndex { get; private set; }
        public int EndIndex { get; private set; }

        public void LoadSettings(ComboBox baudComboBox)
        {
            try
            {
                WebSocketEnabled = true;
                ReverseSteeringInput = false;
                ReverseThrottleInput = false;
                AutoConnectSerial = false;
                SteeringOffset = 0;
                SteeringStepLimit = 180;
                ThrottleStepLimit = 180;
                ThrottleMappings = new List<ControlMappingRange>
                {
                    new ControlMappingRange(0, 180, 0, 180)
                };
                SteeringMappings = new List<ControlMappingRange>
                {
                    new ControlMappingRange(0, 180, 0, 180)
                };
                StartIndex = 0;
                EndIndex = 0;
                

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
                                case "SteeringStepLimit":
                                    if (int.TryParse(parts[1], out var steeringStep))
                                    {
                                        SteeringStepLimit = steeringStep;
                                    }
                                    break;
                                case "ThrottleStepLimit":
                                    if (int.TryParse(parts[1], out var throttleStep))
                                    {
                                        ThrottleStepLimit = throttleStep;
                                    }
                                    break;
                                case "ThrottleMappings":
                                    var throttleMappings = new List<ControlMappingRange>();
                                    var throttleMappingStrs = parts[1].Split('|');
                                    foreach (var mappingStr in throttleMappingStrs)
                                    {
                                        var mapping = ControlMappingRange.FromSettingsString(mappingStr);
                                        if (mapping != null)
                                        {
                                            throttleMappings.Add(mapping);
                                        }
                                    }
                                    if (throttleMappings.Count > 0)
                                    {
                                        ThrottleMappings = throttleMappings;
                                    }
                                    break;
                                case "SteeringMappings":
                                    var steeringMappings = new List<ControlMappingRange>();
                                    var steeringMappingStrs = parts[1].Split('|');
                                    foreach (var mappingStr in steeringMappingStrs)
                                    {
                                        var mapping = ControlMappingRange.FromSettingsString(mappingStr);
                                        if (mapping != null)
                                        {
                                            steeringMappings.Add(mapping);
                                        }
                                    }
                                    if (steeringMappings.Count > 0)
                                    {
                                        SteeringMappings = steeringMappings;
                                    }
                                    break;
                                case "StartIndex":
                                    if (int.TryParse(parts[1], out var sIdx))
                                    {
                                        StartIndex = sIdx;
                                    }
                                    break;
                                case "EndIndex":
                                    if (int.TryParse(parts[1], out var eIdx))
                                    {
                                        EndIndex = eIdx;
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
            int? steeringOffset = null,
            int? steeringStepLimit = null,
            int? throttleStepLimit = null,
            List<ControlMappingRange>? throttleMappings = null,
            List<ControlMappingRange>? steeringMappings = null,
            int? startIndex = null,
            int? endIndex = null)
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
                if (steeringStepLimit.HasValue)
                    SteeringStepLimit = steeringStepLimit.Value;
                if (throttleStepLimit.HasValue)
                    ThrottleStepLimit = throttleStepLimit.Value;
                if (throttleMappings != null && throttleMappings.Count > 0)
                    ThrottleMappings = throttleMappings;
                if (steeringMappings != null && steeringMappings.Count > 0)
                    SteeringMappings = steeringMappings;
                if (startIndex.HasValue)
                    StartIndex = startIndex.Value;
                if (endIndex.HasValue)
                    EndIndex = endIndex.Value;

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
            settings.Add($"SteeringStepLimit={SteeringStepLimit}");
            settings.Add($"ThrottleStepLimit={ThrottleStepLimit}");
            if (ThrottleMappings.Count > 0)
            {
                var mappingStr = string.Join("|", ThrottleMappings.Select(m => m.ToSettingsString()));
                settings.Add($"ThrottleMappings={mappingStr}");
            }
            if (SteeringMappings.Count > 0)
            {
                var mappingStr = string.Join("|", SteeringMappings.Select(m => m.ToSettingsString()));
                settings.Add($"SteeringMappings={mappingStr}");
            }
            settings.Add($"StartIndex={StartIndex}");
            settings.Add($"EndIndex={EndIndex}");

            File.WriteAllLines(SETTINGS_FILE, settings);
        }
    }
}
