using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Fastboot;

/// <summary>
/// Fastboot terminal policy: reading and rebooting are allowed; anything that writes, erases or changes the
/// lock state is refused here and must go through a dedicated function with model/firmware verification.
/// </summary>
public static class FastbootCommandPolicy
{
    private static readonly HashSet<string> Destructive = new(StringComparer.OrdinalIgnoreCase)
    {
        "flash", "flashall", "flash:raw", "update", "erase", "format", "-w", "wipe-super", "flashing",
        "oem", "boot", "set_active", "--set-active", "create-logical-partition", "delete-logical-partition",
        "resize-logical-partition", "snapshot-update", "stage", "fetch", "--disable-verity", "--disable-verification"
    };

    public static CommandDecision Evaluate(string commandLine)
    {
        var args = CommandLine.StripTool(CommandLine.Split(commandLine), "fastboot");
        if (args.Count == 0) return new(CommandVerdict.Blocked, args, "Empty command.");
        if (args.Any(a => a is "-s" or "-c")) return new(CommandVerdict.Blocked, args, "Device selection is done by the program (bottom bar).");

        var top = args[0].ToLowerInvariant();
        if (top == "oem" && args.Count > 1 && args[1].Equals("device-info", StringComparison.OrdinalIgnoreCase))
            return new(CommandVerdict.Allowed, args);

        if (args.Any(a => Destructive.Contains(a)))
            return new(CommandVerdict.Blocked, args,
                "Writing, erasing, booting images and lock/unlock are not allowed from the terminal. Use the dedicated, verified function.");

        return top switch
        {
            "devices" or "getvar" or "help" or "--version" => new(CommandVerdict.Allowed, args),
            "reboot" or "reboot-bootloader" or "continue" => new(CommandVerdict.NeedsConfirmation, args, "The device will restart."),
            _ => new(CommandVerdict.NeedsConfirmation, args, $"'{args[0]}' is not a known read-only command.")
        };
    }
}
