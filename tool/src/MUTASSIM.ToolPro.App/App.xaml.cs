using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MUTASSIM.ToolPro.App.Composition;
using MUTASSIM.ToolPro.Core;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Infrastructure;
using MUTASSIM.ToolPro.Logging;
using Serilog;

namespace MUTASSIM.ToolPro.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        paths.EnsureCreated();

        var terminalSink = new InMemoryLogSink();
        Log.Logger = LoggingSetup.CreateLogger(paths.Logs, terminalSink);
        Log.Information("{Product} {Version} starting ({Runtime}, {Os})", AppInfo.ProductName,
            AppInfo.InformationalVersion, Environment.Version, Environment.OSVersion);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating={Terminating})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        try
        {
            var collection = new ServiceCollection();
            collection.AddToolPro(paths, terminalSink);
            _services = collection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            await Bootstrapper.InitializeAsync(_services);

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show($"MUTASSIM TOOL PRO could not start:\n\n{ex.Message}\n\nSee the log folder:\n{paths.Logs}",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_services is not null)
            {
                // Stop background engines before disposing (DeviceManager is IAsyncDisposable).
                _services.GetService<IDeviceManager>()?.StopAsync().Wait(TimeSpan.FromSeconds(3));
                _services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during shutdown");
        }
        finally
        {
            Log.Information("Application exit code {Code}", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show($"An unexpected error occurred and was logged:\n\n{e.Exception.Message}",
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true; // keep the program alive; the error is in the log.
    }
}
