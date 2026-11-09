namespace MUTASSIM.ToolPro.Core.Platform;

/// <summary>Result of one external tool execution (adb, fastboot…). Nothing is interpreted here.</summary>
public sealed record ProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Elapsed,
    bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;

    /// <summary>stdout + stderr (fastboot writes its normal output to stderr).</summary>
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : StdOut + Environment.NewLine + StdErr;
}

/// <summary>
/// Runs external executables safely: arguments are passed as a list (never through a shell),
/// with timeout, cancellation and process-tree kill.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<string>? onOutputLine = null);
}
