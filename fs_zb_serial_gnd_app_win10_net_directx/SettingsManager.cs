using System.IO;
using Avalonia.Controls;

namespace RCCarController
{
    public class SettingsManager
    {
        private const string SETTINGS_FILE = "settings.ini";

        public string? SavedPort { get; private set; }

        public void LoadSettings(ComboBox baudComboBox)
        {
            try
            {
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

        public void SaveSettings(string? port, object? baudItem)
        {
            try
            {
                var settings = new List<string>();
                if (!string.IsNullOrEmpty(port))
                    settings.Add($"Port={port}");
                if (baudItem != null)
                {
                    string baudValue = (baudItem as ComboBoxItem)?.Content?.ToString() ?? baudItem.ToString();
                    settings.Add($"Baud={baudValue}");
                }

                File.WriteAllLines(SETTINGS_FILE, settings);
            }
            catch (Exception)
            {
                // Handle error
            }
        }
    }
}