using MUTASSIM.ToolPro.Core.Modules;

namespace MUTASSIM.ToolPro.Core.Plugins;

/// <summary>Stable plugin API. Bump only on breaking changes; plugins declare which version they target.</summary>
public static class PluginHostApi
{
    public const int Version = 1;
}

/// <summary>Contents of plugin.json placed next to the plugin assembly.</summary>
public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Vendor { get; set; } = "";
    public int HostApiVersion { get; set; } = PluginHostApi.Version;
    public string EntryAssembly { get; set; } = "";
    public string[] SupportedDevices { get; set; } = [];
    public string[] Services { get; set; } = [];
    public string[] Requirements { get; set; } = [];
}

/// <summary>Implemented by the entry class of every plugin.</summary>
public interface IToolPlugin
{
    /// <summary>Register modules (brands, chipsets, API providers…) into the host.</summary>
    void Register(IModuleRegistry registry);
}

public enum PluginState
{
    Loaded,
    Disabled,
    Incompatible,
    Failed
}

public sealed record PluginInfo(
    string Directory,
    PluginManifest? Manifest,
    PluginState State,
    bool IsSignatureVerified,
    string? Message);

public interface IPluginService
{
    string PluginsDirectory { get; }
    IReadOnlyList<PluginInfo> Plugins { get; }
    Task LoadAllAsync(IModuleRegistry registry, CancellationToken cancellationToken = default);
}
