namespace MUTASSIM_SERVER.Core.Commands;

public enum CommandStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public sealed record DeviceCommand(
    Guid Id,
    string DeviceId,
    string Action,
    DateTime CreatedAt,
    CommandStatus Status
);

/// <summary>
/// Result sent by the agent. DeviceId is optional for backward compatibility
/// with older agents; when supplied the server verifies it owns the command.
/// </summary>
public sealed record CommandResult(
    Guid CommandId,
    bool Success,
    string Message,
    DateTime CompletedAt,
    string? DeviceId = null
);

/// <summary>Full server-side view of a command and (once finished) its result.</summary>
public sealed record CommandRecord(
    DeviceCommand Command,
    CommandResult? Result,
    DateTime? StartedAt
);

public enum CompleteOutcome
{
    Completed,
    NotFound,
    WrongDevice,
    AlreadyCompleted,
    NotRunning
}

public sealed record CreateCommandRequest(
    string DeviceId,
    string Action
);
