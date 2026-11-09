namespace MUTASSIM.ToolPro.Infrastructure;

/// <summary>All per-user data folders. Default root: %LOCALAPPDATA%\MUTASSIM\ToolPro.</summary>
public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MUTASSIM", "ToolPro");
    }

    public string Root { get; }
    public string Logs => Path.Combine(Root, "logs");
    public string Data => Path.Combine(Root, "data");
    public string Firmware => Path.Combine(Root, "firmware");
    public string Plugins => Path.Combine(Root, "plugins");
    public string Exports => Path.Combine(Root, "exports");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string DatabaseFile => Path.Combine(Data, "toolpro.db");

    /// <summary>Bundled tools shipped next to the executable (e.g. tools\platform-tools\adb.exe).</summary>
    public static string BundledToolsDirectory => Path.Combine(AppContext.BaseDirectory, "tools");

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Root, Logs, Data, Firmware, Plugins, Exports })
            Directory.CreateDirectory(dir);
    }
}
