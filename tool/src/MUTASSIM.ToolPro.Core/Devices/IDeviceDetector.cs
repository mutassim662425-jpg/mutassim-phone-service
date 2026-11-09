namespace MUTASSIM.ToolPro.Core.Devices;

/// <summary>
/// A detection engine for one transport (ADB, Fastboot, raw USB/WMI, Apple, serial…).
/// Implementations live in their own modules and are registered through DI or plugins.
/// </summary>
public interface IDeviceDetector
{
    string Name { get; }

    /// <summary>False when a prerequisite is missing (e.g. adb.exe not found).</summary>
    bool IsAvailable { get; }

    /// <summary>Human readable reason when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>Returns every device currently visible to this detector. Must not throw for "no devices".</summary>
    Task<IReadOnlyList<DeviceInfo>> ScanAsync(CancellationToken cancellationToken);
}

public enum DeviceChangeKind
{
    Connected,
    Updated,
    Disconnected
}

public sealed class DeviceChangedEventArgs(DeviceChangeKind kind, DeviceInfo device) : EventArgs
{
    public DeviceChangeKind Kind { get; } = kind;
    public DeviceInfo Device { get; } = device;
}

public sealed record DetectorStatus(
    string Name,
    bool IsAvailable,
    string? Message,
    DateTimeOffset? LastScan,
    string? LastError,
    int DeviceCount);

public interface IDeviceManager
{
    IReadOnlyList<DeviceInfo> Devices { get; }
    IReadOnlyList<DetectorStatus> Detectors { get; }
    bool IsRunning { get; }

    /// <summary>Raised on a background thread. UI consumers must marshal to the dispatcher.</summary>
    event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    /// <summary>Raised after each scan cycle (background thread).</summary>
    event EventHandler? ScanCompleted;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
