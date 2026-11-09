using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Fastboot;

public static class FastbootParsers
{
    /// <summary><c>fastboot devices</c> → serials (lines "SERIAL\tfastboot").</summary>
    public static IReadOnlyList<string> ParseDevices(string output) =>
        output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2 && p[1].StartsWith("fastboot", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Parses <c>getvar</c> output (written to stderr): "(bootloader) key: value" or "key: value".
    /// The key ends at the LAST ':' so "partition-type:system:raw" → key "partition-type:system".
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseGetvar(string output)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("(bootloader)", StringComparison.OrdinalIgnoreCase)) line = line["(bootloader)".Length..].Trim();
            if (line.Length == 0 || line.StartsWith("Finished", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("all:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("getvar:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("FAILED", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("<", StringComparison.Ordinal)) continue;

            var idx = line.LastIndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Length > 0) dict[key] = value;
        }
        return dict;
    }
}

public enum FastbootRebootTarget
{
    System,
    Bootloader,
    Recovery,
    FastbootD
}

public sealed class FastbootClient(IProcessRunner runner, IExternalToolLocator tools, ILogger<FastbootClient> logger)
{
    public ExternalToolStatus Tool => tools.Locate("fastboot");
    public bool IsAvailable => Tool.IsFound;

    public Task<ProcessResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null,
        CancellationToken ct = default, Action<string>? onLine = null)
    {
        var path = Tool.Path ?? throw new InvalidOperationException("fastboot.exe not found. Set the Platform-Tools folder in Settings.");
        return runner.RunAsync(path, args, timeout ?? TimeSpan.FromSeconds(30), ct, onLine);
    }

    public Task<ProcessResult> RunForDeviceAsync(string serial, IReadOnlyList<string> args, TimeSpan? timeout = null,
        CancellationToken ct = default, Action<string>? onLine = null) =>
        RunAsync(["-s", serial, .. args], timeout, ct, onLine);

    public async Task<IReadOnlyList<string>> ListDevicesAsync(CancellationToken ct)
    {
        var r = await RunAsync(["devices"], TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        if (r.TimedOut) throw new TimeoutException("fastboot devices timed out");
        return FastbootParsers.ParseDevices(r.StdOut + "\n" + r.StdErr);
    }

    /// <summary>getvar all; falls back to individual variables on bootloaders that do not support "all".</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetVarsAsync(string serial, CancellationToken ct)
    {
        var all = await RunForDeviceAsync(serial, ["getvar", "all"], TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        var vars = FastbootParsers.ParseGetvar(all.Combined);
        if (vars.Count >= 3) return vars;

        logger.LogDebug("getvar all returned {Count} values on {Serial}; querying individually", vars.Count, serial);
        var merged = new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "product", "serialno", "unlocked", "secure", "is-userspace", "current-slot",
                     "version-bootloader", "version-baseband", "variant", "hw-revision", "anti" })
        {
            var r = await RunForDeviceAsync(serial, ["getvar", name], TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            foreach (var kv in FastbootParsers.ParseGetvar(r.Combined))
                if (kv.Value.Length > 0) merged[kv.Key] = kv.Value;
        }
        return merged;
    }

    public Task<ProcessResult> RebootAsync(string serial, FastbootRebootTarget target, CancellationToken ct)
    {
        string[] args = target switch
        {
            FastbootRebootTarget.System => ["reboot"],
            FastbootRebootTarget.Bootloader => ["reboot", "bootloader"],
            FastbootRebootTarget.Recovery => ["reboot", "recovery"],
            FastbootRebootTarget.FastbootD => ["reboot", "fastboot"],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        return RunForDeviceAsync(serial, args, TimeSpan.FromSeconds(20), ct);
    }
}

public sealed class FastbootDeviceDetector(FastbootClient fastboot, ILogger<FastbootDeviceDetector> logger) : IDeviceDetector
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cache = new();

    public string Name => "Fastboot";
    public bool IsAvailable => fastboot.IsAvailable;
    public string? UnavailableReason => fastboot.Tool.Message;

    public async Task<IReadOnlyList<DeviceInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        var serials = await fastboot.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<DeviceInfo>();
        foreach (var serial in serials)
        {
            if (!_cache.TryGetValue(serial, out var vars))
            {
                try
                {
                    vars = await fastboot.GetVarsAsync(serial, cancellationToken).ConfigureAwait(false);
                    _cache[serial] = vars;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug("getvar on {Serial} failed: {Error}", serial, ex.Message);
                    vars = new Dictionary<string, string>();
                }
            }
            list.Add(FromVars(serial, vars));
        }

        foreach (var stale in _cache.Keys.Except(serials).ToArray()) _cache.TryRemove(stale, out _);
        return list;
    }

    public void Invalidate(string serial) => _cache.TryRemove(serial, out _);

    public static DeviceInfo FromVars(string serial, IReadOnlyDictionary<string, string> vars)
    {
        string? V(string key) => vars.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        var userspace = V("is-userspace") == "yes";
        var unlocked = V("unlocked");
        return new DeviceInfo
        {
            Key = "fastboot:" + serial,
            Mode = userspace ? ConnectionMode.FastbootD : ConnectionMode.Fastboot,
            Source = "Fastboot",
            SerialNumber = serial,
            Product = V("product"),
            Manufacturer = V("manufacturer") ?? V("oem"),
            BootloaderState = unlocked switch
            {
                "yes" => "Unlocked",
                "no" => "Locked",
                _ => V("secure") is { } s ? $"secure: {s}" : null
            },
            Properties = new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase)
        };
    }
}
