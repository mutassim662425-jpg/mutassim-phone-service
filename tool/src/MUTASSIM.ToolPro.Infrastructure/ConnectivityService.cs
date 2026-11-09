using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Infrastructure;

/// <summary>Real connectivity probe: an actual HTTP request, never an assumed state.</summary>
public sealed class ConnectivityService(
    HttpClient http,
    ISettingsService settings,
    ILogger<ConnectivityService> logger) : IConnectivityService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServiceHealth Internet { get; private set; } = ServiceHealth.Unknown;
    public ServiceHealth Server { get; private set; } = ServiceHealth.Unknown;
    public DateTimeOffset? LastChecked { get; private set; }
    public string? LastInternetError { get; private set; }
    private readonly List<string> _probeErrors = [];
    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return; // already running
        try
        {
            Internet = ServiceHealth.Checking;
            Changed?.Invoke(this, EventArgs.Empty);

            var cfg = settings.Current;
            _probeErrors.Clear();
            Internet = await ProbeInternetAsync(cfg.InternetProbeUrl, cancellationToken).ConfigureAwait(false);
            LastInternetError = Internet == ServiceHealth.Online ? null : string.Join(" | ", _probeErrors);
            Server = string.IsNullOrWhiteSpace(cfg.StatusServerUrl)
                ? ServiceHealth.NotConfigured
                : await ProbeAsync(cfg.StatusServerUrl, cancellationToken).ConfigureAwait(false);
            LastChecked = DateTimeOffset.Now;
            logger.LogDebug("Connectivity: internet={Internet} server={Server}", Internet, Server);
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Well-known, highly available HTTPS endpoints used when the configured probe fails.</summary>
    private static readonly string[] FallbackProbes =
    [
        "https://www.google.com/generate_204",
        "https://www.cloudflare.com/cdn-cgi/trace",
        "https://www.microsoft.com/"
    ];

    /// <summary>Online as soon as ANY probe answers; Offline only when all of them fail.</summary>
    private async Task<ServiceHealth> ProbeInternetAsync(string? configured, CancellationToken ct)
    {
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) urls.Add(configured);
        urls.AddRange(FallbackProbes.Where(u => !u.Equals(configured, StringComparison.OrdinalIgnoreCase)));

        foreach (var url in urls)
        {
            if (await ProbeAsync(url, ct).ConfigureAwait(false) == ServiceHealth.Online) return ServiceHealth.Online;
        }

        logger.LogWarning("Internet check failed: none of {Count} endpoints answered", urls.Count);
        return ServiceHealth.Offline;
    }

    private async Task<ServiceHealth> ProbeAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return ServiceHealth.NotConfigured;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if ((int)response.StatusCode < 500) return ServiceHealth.Online;
            _probeErrors.Add($"{uri.Host}: HTTP {(int)response.StatusCode}");
            return ServiceHealth.Offline;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var reason = ex is TaskCanceledException ? "timeout (6 s)" : Flatten(ex);
            _probeErrors.Add($"{uri.Host}: {reason}");
            logger.LogWarning("Probe {Url} failed: {Error}", url, reason);
            return ServiceHealth.Offline;
        }
    }

    /// <summary>Full exception chain, e.g. "HttpRequestException: … → SocketException: …".</summary>
    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        return string.Join(" → ", parts);
    }
}
