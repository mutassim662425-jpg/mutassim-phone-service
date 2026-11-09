using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Core.Plugins;
using MUTASSIM.ToolPro.Database;

namespace MUTASSIM.ToolPro.App.Composition;

/// <summary>Ordered start-up: database → modules → plugins → language.</summary>
public static class Bootstrapper
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrapper");

        var db = services.GetRequiredService<SqliteDatabase>();
        var schema = await db.InitializeAsync();
        logger.LogInformation("Database ready (schema v{Version})", schema);

        var registry = services.GetRequiredService<IModuleRegistry>();
        foreach (var module in services.GetServices<IServiceModule>())
            registry.Register(module);

        var plugins = services.GetRequiredService<IPluginService>();
        await plugins.LoadAllAsync(registry);

        var settings = services.GetRequiredService<ISettingsService>();
        services.GetRequiredService<LocalizationService>().Apply(settings.Current.Language);
    }
}
