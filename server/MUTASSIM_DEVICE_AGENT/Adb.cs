using System.Diagnostics;

namespace MUTASSIM_DEVICE_AGENT;

public sealed record AdbDevice(string Serial, string State, string? Description);

public sealed record AndroidDeviceInfo(
    string Serial,
    string ConnectionStatus,
    string? Brand = null,
    string? Manufacturer = null,
    string? Model = null,
    string? AndroidVersion = null,
    string? SdkVersion = null,
    string? BuildNumber = null);

public sealed class AdbNotFoundException(string message) : Exception(message);

/// <summary>
/// Unified ADB discovery and execution.
/// Search order: MUTASSIM_ADB_PATH (file or folder) -> Desktop\platform-tools
/// (also OneDrive Desktop) -> ANDROID_SDK_ROOT / ANDROID_HOME -> %LOCALAPPDATA%\Android\Sdk -> PATH.
/// </summary>
public static class Adb
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static string ExeName => OperatingSystem.IsWindows() ? "adb.exe" : "adb";

    public static (string? Path, IReadOnlyList<string> Searched) Resolve()
    {
        var searched = new List<string>();

        string? Check(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            candidate = candidate.Trim().Trim('"');

            // Allow MUTASSIM_ADB_PATH to point at the folder instead of adb.exe
            if (Directory.Exists(candidate))
                candidate = Path.Combine(candidate, ExeName);

            searched.Add(candidate);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        var configured = Environment.GetEnvironmentVariable("MUTASSIM_ADB_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var found = Check(configured);
            if (found is not null)
                return (found, searched);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var candidates = new List<string?>
        {
            string.IsNullOrEmpty(desktop) ? null : Path.Combine(desktop, "platform-tools", ExeName),
            Path.Combine(home, "Desktop", "platform-tools", ExeName),
            Path.Combine(home, "OneDrive", "Desktop", "platform-tools", ExeName),
            Combine(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), "platform-tools", ExeName),
            Combine(Environment.GetEnvironmentVariable("ANDROID_HOME"), "platform-tools", ExeName),
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android", "Sdk", "platform-tools", ExeName),
        };

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found = Check(c);
            if (found is not null)
                return (found, searched);
        }

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = Check(Path.Combine(entry.Trim('"'), ExeName));
            if (found is not null)
                return (found, searched);
        }

        return (null, searched);
    }

    private static string? Combine(string? root, params string[] parts) =>
        string.IsNullOrWhiteSpace(root) ? null : Path.Combine([root, .. parts]);

    /// <summary>Resolves ADB or throws a clear, actionable error.</summary>
    public static string RequirePath()
    {
        var (path, searched) = Resolve();
        if (path is not null)
            return path;

        throw new AdbNotFoundException(
            "ADB (adb.exe) not found. Install Android platform-tools to Desktop\\platform-tools " +
            "or set MUTASSIM_ADB_PATH to the full path of adb.exe. Searched: " +
            string.Join("; ", searched.Distinct()));
    }

    public static async Task<string> RunAsync(
        string adbPath, CancellationToken ct, params string[] arguments)
    {
        if (!File.Exists(adbPath))
            throw new AdbNotFoundException($"ADB file not found: {adbPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            WorkingDirectory = Path.GetDirectoryName(adbPath) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start ADB process.");

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }

            if (ct.IsCancellationRequested)
                throw;

            throw new TimeoutException(
                $"ADB timed out after {DefaultTimeout.TotalSeconds:0}s: adb {string.Join(' ', arguments)}");
        }

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                $"adb {string.Join(' ', arguments)} failed (exit {process.ExitCode}): {detail.Trim()}");
        }

        return output.Trim();
    }

    /// <summary>Parses `adb devices -l`. Handles device / unauthorized / offline / no permissions.</summary>
    public static async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(string adbPath, CancellationToken ct)
    {
        var output = await RunAsync(adbPath, ct, "devices", "-l");
        return ParseDevices(output);
    }

    public static IReadOnlyList<AdbDevice> ParseDevices(string output)
    {
        var devices = new List<AdbDevice>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var serial = parts[0];
            var state = parts[1];
            var rest = parts.Skip(2).ToList();

            // "no permissions (...)" is reported as two tokens
            if (state == "no" && rest.Count > 0 && rest[0].StartsWith("permissions", StringComparison.Ordinal))
            {
                state = "no permissions";
                rest.RemoveAt(0);
            }

            devices.Add(new AdbDevice(serial, state, rest.Count == 0 ? null : string.Join(' ', rest)));
        }

        return devices;
    }

    public static async Task<AndroidDeviceInfo> GetDeviceInfoAsync(
        string adbPath, AdbDevice device, CancellationToken ct)
    {
        if (device.State != "device")
            return new AndroidDeviceInfo(device.Serial, device.State);

        // One shell call returns every property: "[key]: [value]"
        var output = await RunAsync(adbPath, ct, "-s", device.Serial, "shell", "getprop");
        var props = ParseGetprop(output);

        string? P(string key) => props.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

        return new AndroidDeviceInfo(
            device.Serial,
            "device",
            P("ro.product.brand"),
            P("ro.product.manufacturer"),
            P("ro.product.model"),
            P("ro.build.version.release"),
            P("ro.build.version.sdk"),
            P("ro.build.display.id"));
    }

    public static Dictionary<string, string> ParseGetprop(string output)
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var sep = line.IndexOf("]: [", StringComparison.Ordinal);
            if (!line.StartsWith('[') || !line.EndsWith(']') || sep < 0)
                continue;

            props[line[1..sep]] = line[(sep + 4)..^1];
        }

        return props;
    }

    public static string StateHint(string state) => state switch
    {
        "unauthorized" => "Unlock the phone and accept the 'Allow USB debugging' prompt.",
        "offline" => "Device offline: reconnect the USB cable or run 'adb kill-server'.",
        "no permissions" => "No USB permission for this device (driver/udev issue).",
        _ => ""
    };
}
