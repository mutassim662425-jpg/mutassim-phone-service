using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Updates;

/// <summary>
/// Phase 1: real version check against a JSON feed (when configured).
/// Phase 8: download + SHA-256 + digital signature verification + installation with user consent.
/// Feed format: { "version": "1.1.0", "notes": "...", "url": "https://...", "sha256": "...", "signature": "..." }
/// </summary>
public sealed class UpdateService(HttpClient http, ISettingsService settings, ILogger<UpdateService> logger)
    : IUpdateService
{
    public UpdateCheckResult LastResult { get; private set; } = new(UpdateState.NotChecked, AppInfo.Version);
    public event EventHandler? Changed;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var feed = settings.Current.UpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(feed) || !Uri.TryCreate(feed, UriKind.Absolute, out var uri))
            return Set(new UpdateCheckResult(UpdateState.NotConfigured, AppInfo.Version,
                Message: "Update server is not configured."));

        if (uri.Scheme != Uri.UriSchemeHttps)
            return Set(new UpdateCheckResult(UpdateState.CheckFailed, AppInfo.Version,
                Message: "Update feed must use HTTPS."));

        Set(new UpdateCheckResult(UpdateState.Checking, AppInfo.Version));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var manifest = await http.GetFromJsonAsync<UpdateFeed>(uri, timeout.Token).ConfigureAwait(false);

            if (manifest?.Version is null || !Version.TryParse(manifest.Version, out var latest))
                return Set(new UpdateCheckResult(UpdateState.CheckFailed, AppInfo.Version, Message: "Invalid update feed."));

            var state = Compare(latest, AppInfo.Version) > 0 ? UpdateState.UpdateAvailable : UpdateState.UpToDate;
            logger.LogInformation("Update check: current {Current}, latest {Latest} → {State}", AppInfo.Version, latest, state);
            return Set(new UpdateCheckResult(state, AppInfo.Version, latest, manifest.Notes));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Update check failed: {Error}", ex.Message);
            return Set(new UpdateCheckResult(UpdateState.CheckFailed, AppInfo.Version, Message: ex.Message));
        }
    }

    /// <summary>Compares versions treating missing components as 0 (1.0 == 1.0.0.0).</summary>
    public static int Compare(Version a, Version b) => Normalize(a).CompareTo(Normalize(b));

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private UpdateCheckResult Set(UpdateCheckResult result)
    {
        LastResult = result;
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private sealed class UpdateFeed
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("signature")] public string? Signature { get; set; }
    }
}
