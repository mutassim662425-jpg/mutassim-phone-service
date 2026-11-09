namespace MUTASSIM.ToolPro.Serial;

/// <summary>A named, ordered list of AT commands. Extensible: new sequences are just new instances.</summary>
public sealed record AtCommandSequence(string Name, IReadOnlyList<AtCommand> Commands)
{
    /// <summary>True only when every command in the sequence is read-only.</summary>
    public bool IsReadOnly => Commands.All(c => c.IsReadOnly);
}

/// <summary>The result of running one sequence: each command's response, in order.</summary>
public sealed record AtSequenceResult(string Name, IReadOnlyList<AtResponse> Responses)
{
    public bool AllOk => Responses.Count > 0 && Responses.All(r => r.IsOk);
    public AtResponse? For(string commandText) =>
        Responses.FirstOrDefault(r => string.Equals(r.Command, commandText, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Runs read-only AT command sequences over an <see cref="AtCommandChannel"/>. A sequence that contains any
/// state-changing command is rejected before anything is sent, so the engine can never be used to run a
/// mixed "read then change" sequence by accident.
/// </summary>
public sealed class AtSequenceRunner(AtCommandChannel channel)
{
    /// <summary>Stop the sequence as soon as a command returns a non-OK status. Default true.</summary>
    public bool StopOnError { get; set; } = true;

    public async Task<AtSequenceResult> RunAsync(AtCommandSequence sequence, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (!sequence.IsReadOnly)
        {
            var bad = sequence.Commands.First(c => !c.IsReadOnly);
            throw new StateChangeRefusedException(bad,
                $"sequence '{sequence.Name}' contains a state-changing command; the engine runs read-only sequences only");
        }

        var responses = new List<AtResponse>(sequence.Commands.Count);
        foreach (var command in sequence.Commands)
        {
            ct.ThrowIfCancellationRequested();
            var response = await channel.SendAsync(command, ct: ct).ConfigureAwait(false);
            responses.Add(response);
            if (StopOnError && !response.IsOk && response.Status != AtStatus.Error)
            {
                // A hard transport failure (timeout/empty) stops the run; a device "ERROR" to one read
                // (unsupported command) is normal and we continue to the next read.
                if (response.Status is AtStatus.Timeout or AtStatus.Empty) break;
            }
        }
        return new AtSequenceResult(sequence.Name, responses);
    }
}
