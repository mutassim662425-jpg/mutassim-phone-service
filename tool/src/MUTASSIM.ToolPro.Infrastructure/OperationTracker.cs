using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Core.Security;

namespace MUTASSIM.ToolPro.Infrastructure;

/// <summary>
/// Persists every operation from start to finish. A scope that is disposed without an explicit
/// result is recorded as Failed — the history can never show a success that did not happen.
/// </summary>
public sealed class OperationTracker(IOperationRepository repository, ILogger<OperationTracker> logger)
    : IOperationTracker
{
    public OperationRecord? LastOperation { get; private set; }
    public event EventHandler<OperationRecord>? OperationFinished;

    public async Task<IOperationScope> BeginAsync(string serviceId, string serviceName, DeviceInfo? device,
        CancellationToken cancellationToken = default)
    {
        var record = new OperationRecord
        {
            ServiceId = serviceId,
            ServiceName = serviceName,
            StartedAt = DateTimeOffset.Now,
            DeviceKeyHash = device is null ? null : DeviceKeyHasher.Hash(device.Key),
            Manufacturer = device?.DisplayManufacturer,
            Model = device?.Model,
            Mode = device?.Mode ?? ConnectionMode.Unknown,
            Status = OperationStatus.Running
        };

        record.Id = await repository.InsertAsync(record, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Operation #{Id} started: {Service}", record.Id, serviceName);
        return new Scope(this, record);
    }

    private async Task FinishAsync(OperationRecord record, OperationStatus status, string message, string? error)
    {
        record.Status = status;
        record.FinishedAt = DateTimeOffset.Now;
        record.ResultMessage = message;
        record.ErrorDetails = error;

        try
        {
            await repository.UpdateAsync(record).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist result of operation #{Id}", record.Id);
        }

        LastOperation = record;
        if (status == OperationStatus.Succeeded)
            logger.LogInformation("Operation #{Id} {Outcome}: {Message}", record.Id, "Success", message);
        else
            logger.LogWarning("Operation #{Id} {Status}: {Message}", record.Id, status, message);

        OperationFinished?.Invoke(this, record);
    }

    private sealed class Scope(OperationTracker owner, OperationRecord record) : IOperationScope
    {
        private int _completed;
        public OperationRecord Record => record;

        public Task SucceedAsync(string message) => Complete(OperationStatus.Succeeded, message, null);

        public Task FailAsync(string message, Exception? exception = null) =>
            Complete(OperationStatus.Failed, message, exception?.ToString());

        public Task CancelAsync(string message) => Complete(OperationStatus.Cancelled, message, null);

        public async ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref _completed) == 0)
                await Complete(OperationStatus.Failed, "Operation ended without reporting a result.", null)
                    .ConfigureAwait(false);
        }

        private Task Complete(OperationStatus status, string message, string? error) =>
            Interlocked.Exchange(ref _completed, 1) == 0
                ? owner.FinishAsync(record, status, message, error)
                : Task.CompletedTask;
    }
}
