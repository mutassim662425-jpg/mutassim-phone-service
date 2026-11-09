using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Infrastructure;

/// <summary>
/// Finds external executables. Search order: user-configured folder → bundled tools folder → PATH.
/// Only reports a path when the file really exists.
/// </summary>
public sealed class ExternalToolLocator(ISettingsService settings) : IExternalToolLocator
{
    public ExternalToolStatus Locate(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var fileName = OperatingSystem.IsWindows() && !toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? toolName + ".exe"
            : toolName;

        foreach (var dir in CandidateDirectories())
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return new ExternalToolStatus(toolName, candidate, null);
            }
            catch (ArgumentException)
            {
                // Invalid characters in a PATH entry: skip it.
            }
        }

        return new ExternalToolStatus(toolName, null,
            $"{fileName} not found. Install Google Android SDK Platform-Tools and set its folder in Settings.");
    }

    private IEnumerable<string> CandidateDirectories()
    {
        var configured = settings.Current.PlatformToolsPath;
        if (!string.IsNullOrWhiteSpace(configured)) yield return configured;

        yield return Path.Combine(AppPaths.BundledToolsDirectory, "platform-tools");
        yield return Path.Combine(AppPaths.BundledToolsDirectory, "heimdall");
        yield return AppPaths.BundledToolsDirectory;

        // Usual places where technicians unpack Google's platform-tools zip.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, "Android", "Sdk", "platform-tools");
        if (!string.IsNullOrEmpty(home))
        {
            foreach (var folder in new[] { "Downloads", "Desktop", "" })
            {
                var basePath = folder.Length == 0 ? home : Path.Combine(home, folder);
                yield return Path.Combine(basePath, "platform-tools");
                yield return Path.Combine(basePath, "platform-tools-latest-windows", "platform-tools");
                yield return Path.Combine(basePath, "heimdall");
            }
        }
        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\platform-tools";
            yield return @"C:\adb";
            yield return @"C:\Android\platform-tools";
            yield return @"C:\heimdall";
            yield return @"C:\Program Files\Heimdall Suite";
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return entry.Trim('"');
    }
}
