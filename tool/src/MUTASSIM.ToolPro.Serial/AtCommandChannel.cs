using System.Diagnostics;
using System.Text;

namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Sends AT commands over an <see cref="ISerialChannel"/> and reads until a final result code or timeout.
///
/// Safety contract: <see cref="SendAsync"/> executes <see cref="AtCommandKind.Read"/> commands only.
/// A <see cref="AtCommandKind.StateChange"/> command is refused (logged, never written to the port). This is
/// how the engine keeps diagnostic reads separate from anything that would change the device or its protection.
/// </summary>
public sealed class AtCommandChannel(ISerialChannel channel, IAtCommandLog? log = null)
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(40);
    public string LineTerminator { get; set; } = "\r";

    public bool IsOpen => channel.IsOpen;
    public string PortName => channel.PortName;

    public void Open() => channel.Open();
    public void Close() => channel.Close();

    /// <summary>
    /// Sends a read-only command and returns the parsed response. Throws
    /// <see cref="StateChangeRefusedException"/> if the command is a state-changing one.
    /// </summary>
    public async Task<AtResponse> SendAsync(AtCommand command, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Kind == AtCommandKind.StateChange)
        {
            const string reason = "state-changing / protection commands are not executed by the diagnostic engine";
            log?.OnRefused(command, reason);
            throw new StateChangeRefusedException(command, reason);
        }

        if (!channel.IsOpen) channel.Open();
        log?.OnSent(command);

        channel.DiscardBuffers();
        channel.Write(command.Text + LineTerminator);

        var response = await ReadUntilFinalAsync(command.Text, timeout ?? DefaultTimeout, ct).ConfigureAwait(false);
        log?.OnReceived(response);
        return response;
    }

    /// <summary>
    /// Sends free-text typed in the AT console. The text is classified first; known engineering / activation /
    /// protection commands are refused exactly like any other state-change command.
    /// </summary>
    public Task<AtResponse> SendConsoleAsync(string rawText, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var command = AtCommandClassifier.Classify(rawText);
        return SendAsync(command, timeout, ct);
    }

    private async Task<AtResponse> ReadUntilFinalAsync(string command, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var buffer = new StringBuilder();

        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = channel.ReadExisting();
            if (chunk.Length > 0)
            {
                buffer.Append(chunk);
                if (HasFinalResultCode(buffer))
                    break;
            }
            else
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
        }

        var lines = SplitLines(buffer.ToString());
        return AtResponseParser.Parse(command, lines, sw.Elapsed);
    }

    private static bool HasFinalResultCode(StringBuilder buffer)
    {
        foreach (var line in SplitLines(buffer.ToString()))
            if (AtResponseParser.IsFinalResultCode(line)) return true;
        return false;
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToList();
}

/// <summary>Thrown when a state-changing / protection AT command is handed to the diagnostic engine.</summary>
public sealed class StateChangeRefusedException(AtCommand command, string reason)
    : InvalidOperationException($"Refusing to execute '{command.Text}': {reason}.")
{
    public AtCommand Command { get; } = command;
}
