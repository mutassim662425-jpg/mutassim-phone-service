namespace MUTASSIM.ToolPro.Core.Platform;

public enum CommandVerdict
{
    Allowed,
    NeedsConfirmation,
    Blocked
}

public sealed record CommandDecision(CommandVerdict Verdict, IReadOnlyList<string> Arguments, string? Reason = null);

/// <summary>Splits a command line into arguments, honouring double quotes. No shell is ever involved.</summary>
public static class CommandLine
{
    public static IReadOnlyList<string> Split(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) args.Add(current.ToString());
                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }
        if (hasToken) args.Add(current.ToString());
        return args;
    }

    /// <summary>Removes a leading "adb"/"adb.exe"/"fastboot"… token typed by habit.</summary>
    public static IReadOnlyList<string> StripTool(IReadOnlyList<string> args, string tool) =>
        args.Count > 0 && (args[0].Equals(tool, StringComparison.OrdinalIgnoreCase) ||
                           args[0].Equals(tool + ".exe", StringComparison.OrdinalIgnoreCase))
            ? args.Skip(1).ToArray()
            : args;
}
