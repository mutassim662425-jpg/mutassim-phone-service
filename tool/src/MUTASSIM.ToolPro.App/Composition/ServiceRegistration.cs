using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.Api;
using MUTASSIM.ToolPro.Api.Assist;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.ViewModels;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Apple;
using MUTASSIM.ToolPro.Assistance;
using MUTASSIM.ToolPro.Core;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.Core.Plugins;
using MUTASSIM.ToolPro.Database;
using MUTASSIM.ToolPro.DeviceManager;
using MUTASSIM.ToolPro.Fastboot;
using MUTASSIM.ToolPro.Firmware;
using MUTASSIM.ToolPro.Honor;
using MUTASSIM.ToolPro.Huawei;
using MUTASSIM.ToolPro.Infrastructure;
using MUTASSIM.ToolPro.Infrastructure.Catalog;
using MUTASSIM.ToolPro.Licensing;
using MUTASSIM.ToolPro.Logging;
using MUTASSIM.ToolPro.MediaTek;
using MUTASSIM.ToolPro.MultiBrand;
using MUTASSIM.ToolPro.Plugins;
using MUTASSIM.ToolPro.Qualcomm;
using MUTASSIM.ToolPro.Samsung;
using MUTASSIM.ToolPro.Updates;
using MUTASSIM.ToolPro.Usb;
using MUTASSIM.ToolPro.Xiaomi;
using Serilog;

namespace MUTASSIM.ToolPro.App.Composition;

/// <summary>Composition root. The only place that knows every concrete implementation.</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddToolPro(this IServiceCollection services, AppPaths paths, InMemoryLogSink terminalSink)
    {
        // ---- Logging (Serilog behind Microsoft.Extensions.Logging) ----
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        // ---- Platform ----
        services.AddSingleton(paths);
        services.AddSingleton(terminalSink);
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton(_ =>
        {
            var http = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MutassimToolPro/{AppInfo.Version.ToString(3)}");
            return http;
        });
        services.AddSingleton<IConnectivityService, ConnectivityService>();
        services.AddSingleton<IExternalToolLocator, ExternalToolLocator>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton(sp => new AssistClient(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton(sp => new MUTASSIM.ToolPro.Api.Account.AccountClient(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<AccountSession>();

        // ---- Data ----
        services.AddSingleton(_ => new SqliteDatabase(paths.DatabaseFile));
        services.AddSingleton<IOperationRepository, SqliteOperationRepository>();
        services.AddSingleton<IDeviceHistoryRepository, SqliteDeviceHistoryRepository>();
        services.AddSingleton<IOperationTracker, OperationTracker>();

        // ---- Engines ----
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<AdbClient>();
        services.AddSingleton<FastbootClient>();
        services.AddSingleton<UsbDeviceDetector>();
        services.AddSingleton<AdbDeviceDetector>();
        services.AddSingleton<FastbootDeviceDetector>();
        services.AddSingleton<IDeviceDetector>(sp => sp.GetRequiredService<UsbDeviceDetector>());
        services.AddSingleton<IDeviceDetector>(sp => sp.GetRequiredService<AdbDeviceDetector>());
        services.AddSingleton<IDeviceDetector>(sp => sp.GetRequiredService<FastbootDeviceDetector>());
        services.AddSingleton<IDeviceManager, DeviceManagerService>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<IPluginService>(sp => new PluginLoader(
            paths.Plugins,
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<PluginLoader>>()));

        // ---- Built-in modules (registered into IModuleRegistry by the Bootstrapper) ----
        services.AddSingleton<IServiceModule, SamsungModule>();
        services.AddSingleton<IServiceModule, XiaomiModule>();
        services.AddSingleton<IServiceModule, HuaweiModule>();
        services.AddSingleton<IServiceModule, HonorModule>();
        services.AddSingleton<IServiceModule, AppleModule>();
        foreach (var profile in BrandProfileModule.CreateBuiltIn())
            services.AddSingleton<IServiceModule>(profile);
        services.AddSingleton<IServiceModule, QualcommModule>();
        services.AddSingleton<IServiceModule, MediaTekModule>();
        services.AddSingleton<IServiceModule, AdbModule>();
        services.AddSingleton<IServiceModule, FastbootModule>();
        services.AddSingleton<IServiceModule, FrpModule>();
        services.AddSingleton<IServiceModule, MdmModule>();
        services.AddSingleton<IServiceModule, FirmwareModule>();
        services.AddSingleton<IServiceModule, UsbDriverModule>();
        services.AddSingleton<IServiceModule, ApiServicesModule>();

        // ---- UI ----
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<OperationLog>();
        services.AddSingleton<ImageLibrary>();
        services.AddSingleton(sp => DeviceCatalog.Load(paths, sp.GetRequiredService<ILoggerFactory>().CreateLogger("DeviceCatalog")));
        services.AddTransient<InfoPanelViewModel>();
        services.AddSingleton<DeviceContext>();
        services.AddSingleton<OperationRunner>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DeviceManagerViewModel>();
        services.AddTransient<FirmwareManagerViewModel>();
        services.AddTransient<OperationHistoryViewModel>();
        services.AddTransient<PluginsViewModel>();
        services.AddTransient<TerminalViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<UsbDriversViewModel>();
        services.AddTransient<AssistViewModel>();
        services.AddTransient<SamsungFlashViewModel>();
        services.AddTransient<AccountViewModel>();

        return services;
    }
}
