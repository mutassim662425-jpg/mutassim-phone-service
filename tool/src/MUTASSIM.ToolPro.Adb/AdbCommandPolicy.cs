using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Adb;

/// <summary>
/// Validates commands typed in the ADB terminal. The target device is always chosen by the program
/// (never by the user via -s), identity/IMEI and destructive block-device writes are refused,
/// and anything that changes the device needs explicit confirmation.
/// </summary>
public static class AdbCommandPolicy
{
    private static readonly HashSet<string> ReadOnlyTop = new(StringComparer.OrdinalIgnoreCase)
    {
        "devices", "version", "get-state", "get-serialno", "get-devpath", "help", "pull", "start-server"
    };

    private static readonly HashSet<string> ReadOnlyShell = new(StringComparer.OrdinalIgnoreCase)
    {
        "getprop", "dumpsys", "ls", "cat", "df", "uname", "id", "ps", "date", "uptime", "whoami", "pwd",
        "wm", "ip", "ifconfig", "free", "lsof", "stat", "du", "find", "grep", "head", "tail", "which", "echo", "env"
    };

    private static readonly string[] BlockedFragments =
    [
        "iphonesubinfo", "persist.radio", "nv write", "nvram", "/efs", "efs.img", "setprop gsm", "setprop ril",
        "rm -rf /", "mkfs", "format", "dd of=/dev", "of=/dev/block", "> /dev/block", "frp", "persistent_data"
    ];

    public static CommandDecision Evaluate(string commandLine)
    {
        var args = CommandLine.StripTool(CommandLine.Split(commandLine), "adb");
        if (args.Count == 0) return new(CommandVerdict.Blocked, args, "Empty command.");

        // Global adb options (-s, -t, -d, -e, -H, -P, -L) come before the command; none are allowed.
        if (args[0].StartsWith('-') || args[0] is "connect" or "disconnect" or "pair" or "tcpip" or "usb")
            return new(CommandVerdict.Blocked, args, "Device selection is done by the program (bottom bar). Remove -s/-t/-d/connect.");

        var joined = string.Join(' ', args).ToLowerInvariant();
        foreach (var f in BlockedFragments)
            if (joined.Contains(f, StringComparison.Ordinal))
                return new(CommandVerdict.Blocked, args, $"Refused: '{f}' touches device identity, protected partitions or wipes storage.");

        var top = args[0].ToLowerInvariant();
        if (ReadOnlyTop.Contains(top)) return new(CommandVerdict.Allowed, args);

        if (top == "shell")
        {
            if (args.Count == 1) return new(CommandVerdict.Blocked, args, "Interactive shell is not supported; type a full command, e.g. shell getprop.");
            var cmd = args[1].ToLowerInvariant();
            if (cmd == "pm" && args.Count > 2 && args[2] is "list" or "path" or "dump") return new(CommandVerdict.Allowed, args);
            if (cmd == "settings" && args.Count > 2 && args[2] is "get" or "list") return new(CommandVerdict.Allowed, args);
            if (ReadOnlyShell.Contains(cmd) && !joined.Contains('>')) return new(CommandVerdict.Allowed, args);
            return new(CommandVerdict.NeedsConfirmation, args, "This shell command can change the device.");
        }

        if (top == "logcat")
            return args.Contains("-d") ? new(CommandVerdict.Allowed, args)
                : new(CommandVerdict.NeedsConfirmation, args, "logcat without -d streams until the timeout stops it.");

        return new(CommandVerdict.NeedsConfirmation, args, $"'{args[0]}' changes the device or its state.");
    }
}
