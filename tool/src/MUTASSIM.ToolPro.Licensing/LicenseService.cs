using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Licensing;

/// <summary>
/// Licence state provider. Phase 1 reports the TRUE state only: "Not configured" until the licence server
/// (Phase 8) exists. There is deliberately no local key comparison — validation must happen server-side
/// (signed token bound to a hardware fingerprint, with an offline grace period).
/// </summary>
public sealed class LicenseService(ISettingsService settings, ILogger<LicenseService> logger) : ILicenseService
{
    public LicenseStatus Current { get; private set; } = new(LicenseState.NotConfigured);
    public event EventHandler? Changed;

    public Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var status = string.IsNullOrWhiteSpace(settings.Current.LicenseServerUrl)
            ? new LicenseStatus(LicenseState.NotConfigured, Message: "Licence server not configured (Phase 8).")
            : new LicenseStatus(LicenseState.NotActivated, Message: "Activation flow arrives in Phase 8.");

        if (status.State != Current.State)
            logger.LogInformation("Licence state: {State}", status.State);

        Current = status;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(status);
    }
}
