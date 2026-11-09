namespace MUTASSIM.ToolPro.Core.Configuration;

/// <summary>User settings persisted as JSON. Contains NO secrets (API keys are stored encrypted separately).</summary>
public sealed class AppSettings
{
    /// <summary>"en" or "ar".</summary>
    public string Language { get; set; } = "en";

    public string? LicenseServerUrl { get; set; }
    public string? UpdateFeedUrl { get; set; }
    public string? StatusServerUrl { get; set; }

    /// <summary>MUTASSIM GSM CLOUD server that recommends solutions (Smart Assist).</summary>
    public string AssistServerUrl { get; set; } = "http://localhost:5179";

    /// <summary>Random id of this tool installation sent to the server (no personal data).</summary>
    public string? AssistClientId { get; set; }

    /// <summary>Lightweight URL used to test internet connectivity.</summary>
    public string InternetProbeUrl { get; set; } = "https://www.google.com/generate_204";

    /// <summary>Folder containing adb.exe / fastboot.exe (Google Android SDK Platform-Tools).</summary>
    public string? PlatformToolsPath { get; set; }

    public int DeviceScanIntervalSeconds { get; set; } = 2;

    /// <summary>User consent to keep device serial numbers in history. Off by default.</summary>
    public bool StoreDeviceSerials { get; set; }

    /// <summary>Allow loading plugins without a verified signature (development only).</summary>
    public bool AllowUnsignedPlugins { get; set; }

    public string? LastFirmwareFolder { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler? Changed;
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
