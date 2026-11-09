using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Infrastructure;

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<string>? onOutputLine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var sw = Stopwatch.StartNew();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr) stderr.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Could not start {fileName}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            process.WaitForExit(); // flush async output handlers
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
            if (!timedOut) throw;
        }

        sw.Stop();
        string outText, errText;
        lock (stdout) outText = stdout.ToString().TrimEnd();
        lock (stderr) errText = stderr.ToString().TrimEnd();
        var exit = timedOut ? -1 : process.ExitCode;

        logger.LogDebug("{Tool} {Args} → exit {Exit} in {Ms} ms{Timeout}", Path.GetFileName(fileName),
            string.Join(' ', arguments), exit, sw.ElapsedMilliseconds, timedOut ? " (TIMEOUT)" : "");
        return new ProcessResult(exit, outText, errText, sw.Elapsed, timedOut);
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(ex, "Kill failed");
        }
    }
}
