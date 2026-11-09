using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.Core.Operations;

public enum OperationStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>One row of the operation history. Contains no serial/IMEI unless the user consented.</summary>
public sealed class OperationRecord
{
    public long Id { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public required string ServiceId { get; set; }
    public required string ServiceName { get; set; }
    public string? DeviceKeyHash { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public ConnectionMode Mode { get; set; } = ConnectionMode.Unknown;
    public OperationStatus Status { get; set; } = OperationStatus.Running;
    public string? ResultMessage { get; set; }
    public string? ErrorDetails { get; set; }

    public TimeSpan? Duration => FinishedAt is { } f ? f - StartedAt : null;
}

public sealed record OperationQuery(
    string? Text = null,
    OperationStatus? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Limit = 500);

public interface IOperationRepository
{
    Task<long> InsertAsync(OperationRecord record, CancellationToken cancellationToken = default);
    Task UpdateAsync(OperationRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationRecord>> QueryAsync(OperationQuery query, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(long id, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Records every service execution. Scope must be completed explicitly; disposal without a result = Failed.</summary>
public interface IOperationTracker
{
    OperationRecord? LastOperation { get; }
    event EventHandler<OperationRecord>? OperationFinished;
    Task<IOperationScope> BeginAsync(string serviceId, string serviceName, DeviceInfo? device,
        CancellationToken cancellationToken = default);
}

public interface IOperationScope : IAsyncDisposable
{
    OperationRecord Record { get; }
    Task SucceedAsync(string message);
    Task FailAsync(string message, Exception? exception = null);
    Task CancelAsync(string message);
}

public sealed record DeviceHistoryEntry(
    string KeyHash,
    string? Manufacturer,
    string? Model,
    ConnectionMode Mode,
    DateTimeOffset LastSeen);

public interface IDeviceHistoryRepository
{
    Task UpsertAsync(DeviceInfo device, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceHistoryEntry>> RecentAsync(int limit, CancellationToken cancellationToken = default);
}
