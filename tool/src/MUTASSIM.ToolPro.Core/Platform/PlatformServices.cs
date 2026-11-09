namespace MUTASSIM.ToolPro.Core.Platform;

public enum ServiceHealth
{
    Unknown,
    Checking,
    Online,
    Offline,
    NotConfigured
}

public interface IConnectivityService
{
    ServiceHealth Internet { get; }
    ServiceHealth Server { get; }
    DateTimeOffset? LastChecked { get; }

    /// <summary>Exact reason of the last failed internet check (null when online).</summary>
    string? LastInternetError { get; }
    event EventHandler? Changed;
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

// ---------- Licensing ----------

public enum LicenseState
{
    /// <summary>No licence server configured yet (Phase 8).</summary>
    NotConfigured,
    NotActivated,
    Active,
    Expired,
    Invalid,
    ServerUnreachable
}

public sealed record LicenseStatus(
    LicenseState State,
    string? Plan = null,
    DateTimeOffset? ExpiresAt = null,
    string? Message = null);

public interface ILicenseService
{
    LicenseStatus Current { get; }
    event EventHandler? Changed;
    Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken = default);
}

// ---------- Updates ----------

public enum UpdateState
{
    NotChecked,
    Checking,
    NotConfigured,
    UpToDate,
    UpdateAvailable,
    CheckFailed
}

public sealed record UpdateCheckResult(
    UpdateState State,
    Version Current,
    Version? Latest = null,
    string? ReleaseNotes = null,
    string? Message = null);

public interface IUpdateService
{
    UpdateCheckResult LastResult { get; }
    event EventHandler? Changed;
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

// ---------- External tools ----------

public sealed record ExternalToolStatus(string ToolName, string? Path, string? Message)
{
    public bool IsFound => Path is not null;
}

public interface IExternalToolLocator
{
    /// <summary>Find an executable (e.g. "adb", "fastboot") in the configured folder, the app's tools folder, then PATH.</summary>
    ExternalToolStatus Locate(string toolName);
}
