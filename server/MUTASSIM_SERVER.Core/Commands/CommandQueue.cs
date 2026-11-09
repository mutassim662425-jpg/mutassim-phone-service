namespace MUTASSIM_SERVER.Core.Commands;

/// <summary>
/// Thread-safe in-memory command queue.
/// Lifecycle: Pending -> (GetNext) Running -> (Complete) Completed | Failed.
/// All state transitions happen under one lock, so a command can be handed
/// out only once and completed only once, and results are kept.
/// </summary>
public sealed class CommandQueue
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, CommandRecord> _records = new();
    private long _sequence;
    private readonly Dictionary<Guid, long> _order = new();

    public DeviceCommand Add(string deviceId, string action)
    {
        var command = new DeviceCommand(
            Guid.NewGuid(),
            deviceId,
            action,
            DateTime.UtcNow,
            CommandStatus.Pending
        );

        lock (_sync)
        {
            _records[command.Id] = new CommandRecord(command, null, null);
            _order[command.Id] = ++_sequence;
        }

        return command;
    }

    /// <summary>
    /// Atomically takes the oldest pending command for the device and marks
    /// it Running. Returns null when there is nothing to do.
    /// </summary>
    public DeviceCommand? GetNext(string deviceId)
    {
        lock (_sync)
        {
            CommandRecord? next = null;
            long nextOrder = long.MaxValue;

            foreach (var record in _records.Values)
            {
                if (record.Command.Status != CommandStatus.Pending ||
                    !string.Equals(record.Command.DeviceId, deviceId, StringComparison.Ordinal))
                    continue;

                var order = _order[record.Command.Id];
                if (order < nextOrder)
                {
                    next = record;
                    nextOrder = order;
                }
            }

            if (next is null)
                return null;

            var running = next.Command with { Status = CommandStatus.Running };
            _records[running.Id] = next with { Command = running, StartedAt = DateTime.UtcNow };
            return running;
        }
    }

    public CompleteOutcome Complete(CommandResult result)
    {
        lock (_sync)
        {
            if (!_records.TryGetValue(result.CommandId, out var record))
                return CompleteOutcome.NotFound;

            if (result.DeviceId is not null &&
                !string.Equals(result.DeviceId, record.Command.DeviceId, StringComparison.Ordinal))
                return CompleteOutcome.WrongDevice;

            if (record.Command.Status is CommandStatus.Completed or CommandStatus.Failed)
                return CompleteOutcome.AlreadyCompleted;

            if (record.Command.Status != CommandStatus.Running)
                return CompleteOutcome.NotRunning;

            var status = result.Success ? CommandStatus.Completed : CommandStatus.Failed;
            _records[result.CommandId] = record with
            {
                Command = record.Command with { Status = status },
                Result = result
            };

            return CompleteOutcome.Completed;
        }
    }

    public CommandRecord? Get(Guid commandId)
    {
        lock (_sync)
        {
            return _records.TryGetValue(commandId, out var record) ? record : null;
        }
    }

    public IReadOnlyList<CommandRecord> List(string? deviceId = null, int max = 100)
    {
        lock (_sync)
        {
            return _records.Values
                .Where(r => deviceId is null || r.Command.DeviceId == deviceId)
                .OrderByDescending(r => _order[r.Command.Id])
                .Take(max)
                .ToList();
        }
    }
}
