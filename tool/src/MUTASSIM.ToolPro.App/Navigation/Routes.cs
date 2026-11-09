using Microsoft.Extensions.DependencyInjection;
using MUTASSIM.ToolPro.App.ViewModels;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Core.Modules;

namespace MUTASSIM.ToolPro.App.Navigation;

/// <summary>A tab or a tool button. Workspace tabs carry their own bilingual titles.</summary>
public sealed record NavEntry(string Route, string? TitleKey, string Glyph, WorkspaceDefinition? Workspace = null);

public static class Routes
{
    public const string Home = "home";
    public const string Devices = "devices";
    public const string Firmware = "firmware";
    public const string Drivers = "drivers";
    public const string History = "history";
    public const string Plugins = "plugins";
    public const string Api = "api";
    public const string Terminal = "terminal";
    public const string Settings = "settings";
    public const string About = "about";
    public const string Assist = "assist";
    public const string SamsungFlash = "samflash";
    public const string Account = "account";
    public const string AtConsole = "atconsole";

    /// <summary>Page opened at start-up (the HOME button was removed).</summary>
    public const string Start = "ws.samsung";

    /// <summary>Top tab strip: Home + every workspace.</summary>
    public static readonly IReadOnlyList<NavEntry> Tabs =
    [
        new(Home, "Nav.Home", ""),
        .. WorkspaceCatalog.All.Select(w => new NavEntry(w.Route, null, "", w))
    ];

    /// <summary>Tool buttons in the title bar.</summary>
    public static readonly IReadOnlyList<NavEntry> Tools =
    [
        new(Account, "Nav.Account", "\uE77B"),
        new(Assist, "Nav.Assist", "\uE753"),
        new(AtConsole, "Nav.AtConsole", "\uE756"),
        new(Devices, "Nav.DeviceManager", ""),
        new(Firmware, "Nav.Firmware", ""),
        new(Drivers, "Nav.UsbDrivers", ""),
        new(History, "Nav.History", ""),
        new(Terminal, "Nav.Terminal", ""),
        new(Plugins, "Nav.Plugins", ""),
        new(Api, "Nav.Api", ""),
        new(About, "Nav.About", "")
    ];

    public static void RegisterAll(NavigationService navigation)
    {
        navigation.Register(Home, sp => sp.GetRequiredService<DashboardViewModel>());
        navigation.Register(Devices, sp => sp.GetRequiredService<DeviceManagerViewModel>());
        navigation.Register(Firmware, sp => sp.GetRequiredService<FirmwareManagerViewModel>());
        navigation.Register(Drivers, sp => sp.GetRequiredService<UsbDriversViewModel>());
        navigation.Register(History, sp => sp.GetRequiredService<OperationHistoryViewModel>());
        navigation.Register(Plugins, sp => sp.GetRequiredService<PluginsViewModel>());
        navigation.Register(Terminal, sp => sp.GetRequiredService<TerminalViewModel>());
        navigation.Register(Settings, sp => sp.GetRequiredService<SettingsViewModel>());
        navigation.Register(About, sp => sp.GetRequiredService<AboutViewModel>());
        navigation.Register(Assist, sp => sp.GetRequiredService<AssistViewModel>());
        navigation.Register(SamsungFlash, sp => sp.GetRequiredService<SamsungFlashViewModel>());
        navigation.Register(Account, sp => sp.GetRequiredService<AccountViewModel>());
        navigation.Register(AtConsole, sp => sp.GetRequiredService<AtConsoleViewModel>());
        navigation.Register(Api, sp => ActivatorUtilities.CreateInstance<ModulePageViewModel>(
            sp, new[] { ModuleIds.ApiServices }, "Nav.Api"));

        foreach (var ws in WorkspaceCatalog.All)
        {
            var definition = ws;
            navigation.Register(ws.Route, sp => ActivatorUtilities.CreateInstance<WorkspaceViewModel>(sp, definition));
        }
    }

    /// <summary>Tab that shows the given module (for "open compatible services").</summary>
    public static string? RouteForModule(string moduleId) => WorkspaceCatalog.RouteForModule(moduleId);
}
