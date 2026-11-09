using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Adb;

public enum AdbRebootTarget
{
    System,
    Recovery,
    Bootloader,
    FastbootD,
    Download,
    Sideload,
    Edl
}

/// <summary>Thin, honest wrapper over Google's adb.exe. Every call returns the real tool output.</summary>
public sealed class AdbClient(IProcessRunner runner, IExternalToolLocator tools, ILogger<AdbClient> logger)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public ExternalToolStatus Tool => tools.Locate("adb");
    public bool IsAvailable => Tool.IsFound;

    public Task<ProcessResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null,
        CancellationToken ct = default, Action<string>? onLine = null)
    {
        var path = Tool.Path ?? throw new InvalidOperationException("adb.exe not found. Set the Platform-Tools folder in Settings.");
        return runner.RunAsync(path, args, timeout ?? DefaultTimeout, ct, onLine);
    }

    public Task<ProcessResult> RunForDeviceAsync(string serial, IReadOnlyList<string> args, TimeSpan? timeout = null,
        CancellationToken ct = default, Action<string>? onLine = null) =>
        RunAsync(["-s", serial, .. args], timeout, ct, onLine);

    public async Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(CancellationToken ct)
    {
        var r = await RunAsync(["devices", "-l"], TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        if (!r.Success)
        {
            logger.LogWarning("adb devices failed (exit {Exit}): {Err}", r.ExitCode, r.StdErr);
            throw new InvalidOperationException($"adb devices failed: {FirstLine(r.StdErr)}");
        }
        return AdbParsers.ParseDevices(r.StdOut);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPropsAsync(string serial, CancellationToken ct)
    {
        var r = await RunForDeviceAsync(serial, ["shell", "getprop"], TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        if (!r.Success) throw new InvalidOperationException($"getprop failed: {FirstLine(r.Combined)}");
        return AdbParsers.ParseGetprop(r.StdOut);
    }

    public async Task<string?> ShellAsync(string serial, IReadOnlyList<string> command, CancellationToken ct)
    {
        var r = await RunForDeviceAsync(serial, ["shell", .. command], DefaultTimeout, ct).ConfigureAwait(false);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public Task<ProcessResult> RebootAsync(string serial, AdbRebootTarget target, CancellationToken ct)
    {
        string[] args = target switch
        {
            AdbRebootTarget.System => ["reboot"],
            AdbRebootTarget.Recovery => ["reboot", "recovery"],
            AdbRebootTarget.Bootloader => ["reboot", "bootloader"],
            AdbRebootTarget.FastbootD => ["reboot", "fastboot"],
            AdbRebootTarget.Download => ["reboot", "download"],
            AdbRebootTarget.Sideload => ["reboot", "sideload"],
            AdbRebootTarget.Edl => ["reboot", "edl"],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        return RunForDeviceAsync(serial, args, TimeSpan.FromSeconds(20), ct);
    }

    public async Task<IReadOnlyList<string>> ListPackagesAsync(string serial, bool userOnly, CancellationToken ct)
    {
        string[] args = userOnly ? ["shell", "pm", "list", "packages", "-3"] : ["shell", "pm", "list", "packages"];
        var r = await RunForDeviceAsync(serial, args, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        if (!r.Success) throw new InvalidOperationException($"pm list packages failed: {FirstLine(r.Combined)}");
        return AdbParsers.ParsePackages(r.StdOut);
    }

    public Task<ProcessResult> InstallAsync(string serial, string apkPath, CancellationToken ct, Action<string>? onLine = null) =>
        RunForDeviceAsync(serial, ["install", "-r", apkPath], TimeSpan.FromMinutes(5), ct, onLine);

    /// <summary>Uninstalls only a package that is currently listed as a user (third-party) app.</summary>
    public async Task<ProcessResult> UninstallUserAppAsync(string serial, string package, CancellationToken ct)
    {
        var userApps = await ListPackagesAsync(serial, userOnly: true, ct).ConfigureAwait(false);
        if (!userApps.Contains(package, StringComparer.Ordinal))
            throw new InvalidOperationException($"'{package}' is not an installed user app; system apps are not removed.");
        return await RunForDeviceAsync(serial, ["uninstall", package], TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
    }

    public static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "no output";
}
