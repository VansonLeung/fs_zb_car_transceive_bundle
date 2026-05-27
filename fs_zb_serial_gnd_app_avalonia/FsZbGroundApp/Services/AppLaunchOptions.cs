using System.Collections.Generic;

namespace FsZbGroundApp.Services;

public sealed class AppLaunchOptions
{
    public static AppLaunchOptions Default { get; } = new();

    public bool AutoStartWfb { get; init; }

    public bool ExitAfterAutomation { get; init; }

    public bool EnableConsoleLogging { get; init; }

    public string? VidPid { get; init; }

    public int? Channel { get; init; }

    public string ChannelWidth { get; init; } = "20";

    public string Codec { get; init; } = "AUTO";

    public string? KeyPath { get; init; }

    public static AppLaunchOptions Parse(IReadOnlyList<string> args)
    {
        var autoStart = false;
        var exitAfterAutomation = false;
        var enableConsoleLogging = false;
        string? vidPid = null;
        int? channel = null;
        string channelWidth = "20";
        string codec = "AUTO";
        string? keyPath = null;

        for (var index = 0; index < args.Count; index++)
        {
            var current = args[index];
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            switch (current.Trim())
            {
                case "--auto-start-wfb":
                    autoStart = true;
                    break;
                case "--wfb-exit":
                    exitAfterAutomation = true;
                    break;
                case "--console-log":
                    enableConsoleLogging = true;
                    break;
                case "--wfb-vidpid":
                    if (TryGetValue(args, ref index, out var vidPidValue))
                    {
                        vidPid = vidPidValue;
                    }
                    break;
                case "--wfb-channel":
                    if (TryGetValue(args, ref index, out var channelValue)
                        && int.TryParse(channelValue, out var parsedChannel))
                    {
                        channel = parsedChannel;
                    }
                    break;
                case "--wfb-width":
                    if (TryGetValue(args, ref index, out var widthValue))
                    {
                        channelWidth = widthValue;
                    }
                    break;
                case "--wfb-codec":
                    if (TryGetValue(args, ref index, out var codecValue))
                    {
                        codec = codecValue;
                    }
                    break;
                case "--wfb-key":
                    if (TryGetValue(args, ref index, out var keyValue))
                    {
                        keyPath = keyValue;
                    }
                    break;
            }
        }

        return new AppLaunchOptions
        {
            AutoStartWfb = autoStart,
            ExitAfterAutomation = exitAfterAutomation,
            EnableConsoleLogging = enableConsoleLogging || autoStart,
            VidPid = vidPid,
            Channel = channel,
            ChannelWidth = string.IsNullOrWhiteSpace(channelWidth) ? "20" : channelWidth.Trim(),
            Codec = string.IsNullOrWhiteSpace(codec) ? "AUTO" : codec.Trim().ToUpperInvariant(),
            KeyPath = string.IsNullOrWhiteSpace(keyPath) ? null : keyPath.Trim()
        };
    }

    private static bool TryGetValue(IReadOnlyList<string> args, ref int index, out string value)
    {
        if (index + 1 < args.Count)
        {
            index++;
            value = args[index];
            return true;
        }

        value = string.Empty;
        return false;
    }
}