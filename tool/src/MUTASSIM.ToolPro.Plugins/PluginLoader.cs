using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Core.Plugins;

namespace MUTASSIM.ToolPro.Plugins;

/// <summary>
/// Discovers plugins in &lt;plugins&gt;\&lt;PluginFolder&gt;\plugin.json, checks API compatibility and loads
/// each plugin into its own AssemblyLoadContext. A failing plugin never affects the host or other plugins.
///
/// Signature verification is implemented in Phase 8; until then unsigned plugins are refused unless the
/// user explicitly enables "Allow unsigned plugins" (development only).
/// </summary>
public sealed class PluginLoader(
    string pluginsDirectory,
    ISettingsService settings,
    ILogger<PluginLoader> logger) : IPluginService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly List<PluginInfo> _plugins = [];

    public string PluginsDirectory { get; } = pluginsDirectory;
    public IReadOnlyList<PluginInfo> Plugins => _plugins.ToArray();

    public Task LoadAllAsync(IModuleRegistry registry, CancellationToken cancellationToken = default)
    {
        _plugins.Clear();
        if (!Directory.Exists(PluginsDirectory))
        {
            logger.LogInformation("Plugins folder not found: {Dir}", PluginsDirectory);
            return Task.CompletedTask;
        }

        foreach (var dir in Directory.EnumerateDirectories(PluginsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _plugins.Add(LoadOne(dir, registry));
        }

        logger.LogInformation("Plugin scan complete: {Loaded}/{Total} loaded",
            _plugins.Count(p => p.State == PluginState.Loaded), _plugins.Count);
        return Task.CompletedTask;
    }

    private PluginInfo LoadOne(string dir, IModuleRegistry registry)
    {
        var manifestPath = Path.Combine(dir, "plugin.json");
        PluginManifest? manifest = null;
        try
        {
            if (!File.Exists(manifestPath))
                return new PluginInfo(dir, null, PluginState.Failed, false, "plugin.json missing");

            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                return new PluginInfo(dir, manifest, PluginState.Failed, false, "Invalid manifest (Id/EntryAssembly required)");

            if (manifest.HostApiVersion != PluginHostApi.Version)
                return new PluginInfo(dir, manifest, PluginState.Incompatible, false,
                    $"Plugin targets host API v{manifest.HostApiVersion}; this build provides v{PluginHostApi.Version}");

            const bool signatureVerified = false; // Phase 8: Authenticode / detached signature check.
            if (!signatureVerified && !settings.Current.AllowUnsignedPlugins)
                return new PluginInfo(dir, manifest, PluginState.Disabled, false,
                    "Unsigned plugin refused. Enable 'Allow unsigned plugins' in Settings for development only.");

            var assemblyPath = Path.GetFullPath(Path.Combine(dir, manifest.EntryAssembly));
            if (!assemblyPath.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase) || !File.Exists(assemblyPath))
                return new PluginInfo(dir, manifest, PluginState.Failed, false, "Entry assembly not found inside the plugin folder");

            var context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var entryTypes = assembly.GetExportedTypes()
                .Where(t => typeof(IToolPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                .ToArray();

            if (entryTypes.Length == 0)
                return new PluginInfo(dir, manifest, PluginState.Failed, false, "No public IToolPlugin implementation found");

            foreach (var type in entryTypes)
            {
                var plugin = (IToolPlugin)Activator.CreateInstance(type)!;
                plugin.Register(registry);
            }

            logger.LogInformation("Plugin loaded: {Name} {Version} by {Vendor}", manifest.Name, manifest.Version, manifest.Vendor);
            return new PluginInfo(dir, manifest, PluginState.Loaded, signatureVerified, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin in {Dir} failed to load", dir);
            return new PluginInfo(dir, manifest, PluginState.Failed, false, ex.Message);
        }
    }

    /// <summary>Isolated context; shared contracts (MUTASSIM.ToolPro.Core) always resolve to the host's copy.</summary>
    private sealed class PluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } name && name.StartsWith("MUTASSIM.ToolPro.Core", StringComparison.Ordinal))
                return null; // fall back to the default (host) context → shared type identity

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
