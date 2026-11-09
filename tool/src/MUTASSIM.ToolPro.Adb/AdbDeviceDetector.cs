using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.Adb;

/// <summary>
/// Detects devices visible to adb (normal, unauthorized, offline, recovery, sideload).
/// Properties are read once per connection (cached by serial + state) to keep polling light.
/// </summary>
public sealed class AdbDeviceDetector(AdbClient adb, ILogger<AdbDeviceDetector> logger) : IDeviceDetector
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _propsCache = new();

    public string Name => "ADB";
    public bool IsAvailable => adb.IsAvailable;
    public string? UnavailableReason => adb.Tool.Message;

    public async Task<IReadOnlyList<DeviceInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        var entries = await adb.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<DeviceInfo>(entries.Count);
        var liveKeys = new HashSet<string>();

        foreach (var entry in entries)
        {
            var mode = MapState(entry.State);
            var cacheKey = $"{entry.Serial}|{mode}";
            liveKeys.Add(cacheKey);

            IReadOnlyDictionary<string, string> props = new Dictionary<string, string>();
            if (mode is ConnectionMode.Adb or ConnectionMode.Recovery)
            {
                if (!_propsCache.TryGetValue(cacheKey, out var cached))
                {
                    try
                    {
                        cached = await adb.GetPropsAsync(entry.Serial, cancellationToken).ConfigureAwait(false);
                        _propsCache[cacheKey] = cached;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogDebug("getprop on {Serial} failed: {Error}", entry.Serial, ex.Message);
                        cached = new Dictionary<string, string>();
                    }
                }
                props = cached;
            }

            result.Add(AndroidDeviceMapper.FromAdb(entry, mode, props));
        }

        foreach (var stale in _propsCache.Keys.Where(k => !liveKeys.Contains(k)).ToArray())
            _propsCache.TryRemove(stale, out _);

        return result;
    }

    /// <summary>Forget cached properties so the next scan re-reads them (used by "Read Info").</summary>
    public void Invalidate(string serial)
    {
        foreach (var key in _propsCache.Keys.Where(k => k.StartsWith(serial + "|", StringComparison.Ordinal)).ToArray())
            _propsCache.TryRemove(key, out _);
    }

    public static ConnectionMode MapState(string state) => state.Trim().ToLowerInvariant() switch
    {
        "device" => ConnectionMode.Adb,
        "unauthorized" => ConnectionMode.AdbUnauthorized,
        "offline" => ConnectionMode.AdbOffline,
        "recovery" => ConnectionMode.Recovery,
        "sideload" => ConnectionMode.Sideload,
        "bootloader" => ConnectionMode.Fastboot,
        _ => ConnectionMode.AdbOffline // "no permissions", "authorizing", "connecting"… → not usable yet
    };
}

/// <summary>Builds a <see cref="DeviceInfo"/> strictly from what the device reported.</summary>
public static class AndroidDeviceMapper
{
    public static DeviceInfo FromAdb(AdbDeviceEntry entry, ConnectionMode mode, IReadOnlyDictionary<string, string> props)
    {
        string? P(string key) => props.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var verified = P("ro.boot.verifiedbootstate");
        var locked = P("ro.boot.flash.locked");
        string? bootloader = (locked, verified) switch
        {
            ("1", _) => $"Locked{(verified is null ? "" : $" ({verified})")}",
            ("0", _) => $"Unlocked{(verified is null ? "" : $" ({verified})")}",
            (null, "green") => "Locked (green)",
            (null, "orange") => "Unlocked (orange)",
            (null, { } v) => v,
            _ => null
        };

        var family = ChipsetDetector.FromProperties(props);
        var extra = new Dictionary<string, string>(props, StringComparer.Ordinal)
        {
            ["adb.state"] = entry.State
        };
        if (family != ChipsetFamily.Unknown) extra["chipset.family"] = family.ToString();

        return new DeviceInfo
        {
            Key = "adb:" + entry.Serial,
            Mode = mode,
            Source = "ADB",
            SerialNumber = entry.Serial,
            Manufacturer = P("ro.product.manufacturer"),
            Brand = P("ro.product.brand"),
            Model = P("ro.product.model") ?? entry.Model,
            ModelNumber = P("ro.product.device") ?? entry.DeviceCodename,
            Product = P("ro.product.name") ?? entry.Product,
            BuildNumber = P("ro.build.display.id"),
            AndroidVersion = P("ro.build.version.release"),
            SecurityPatch = P("ro.build.version.security_patch"),
            Chipset = ChipsetDetector.DescribeChip(props) is { } chip
                ? (family == ChipsetFamily.Unknown ? chip : $"{chip} [{family}]")
                : null,
            CpuAbi = P("ro.product.cpu.abi"),
            BootloaderState = bootloader,
            Properties = extra
        };
    }
}
