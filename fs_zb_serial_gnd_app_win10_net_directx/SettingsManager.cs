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

        public void LoadSettings(ComboBox baudComboBox)
        {
            try
            {
                WebSocketEnabled = true;
                ReverseSteeringInput = false;
                ReverseThrottleInput = false;

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
            bool? reverseThrottle = null)
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

                var settings = new List<string>();
                if (!string.IsNullOrEmpty(SavedPort))
                    settings.Add($"Port={SavedPort}");
                if (!string.IsNullOrEmpty(SavedBaud))
                    settings.Add($"Baud={SavedBaud}");
                settings.Add($"WebSocketEnabled={WebSocketEnabled}");
                settings.Add($"ReverseSteering={ReverseSteeringInput}");
                settings.Add($"ReverseThrottle={ReverseThrottleInput}");

                File.WriteAllLines(SETTINGS_FILE, settings);
            }
            catch (Exception)
            {
                // Handle error
            }
        }
    }
}
