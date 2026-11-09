using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.Infrastructure.Catalog;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed record ShortcutItem(string Route, string Title, string Glyph);

/// <summary>
/// Home: Platform-Tools setup, connected devices, live device card, system status and tool shortcuts.
/// Brand navigation lives in the brand bar of the main window.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private const string PlatformToolsPage = "https://developer.android.com/tools/releases/platform-tools";

    private readonly IDeviceManager _devices;
    private readonly IConnectivityService _connectivity;
    private readonly ILicenseService _license;
    private readonly IModuleRegistry _modules;
    private readonly NavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly IExternalToolLocator _tools;
    private readonly IDialogService _dialogs;
    private readonly MainViewModel _shell;
    private readonly OperationLog _log;
    private bool _disposed;

    public DashboardViewModel(
        IDeviceManager devices,
        IConnectivityService connectivity,
        ILicenseService license,
        IModuleRegistry modules,
        NavigationService navigation,
        ISettingsService settings,
        IExternalToolLocator tools,
        IDialogService dialogs,
        MainViewModel shell,
        OperationLog log,
        DeviceContext deviceContext,
        DeviceCatalog catalog,
        InfoPanelViewModel info)
    {
        _devices = devices;
        _connectivity = connectivity;
        _license = license;
        _modules = modules;
        _navigation = navigation;
        _settings = settings;
        _tools = tools;
        _dialogs = dialogs;
        _shell = shell;
        _log = log;
        DeviceContext = deviceContext;
        Info = info;
        CatalogCount = catalog.Models.Count;

        Shortcuts =
        [
            new(Routes.Devices, LocalizationService.Get("Nav.DeviceManager"), ""),
            new(Routes.Firmware, LocalizationService.Get("Nav.Firmware"), ""),
            new(Routes.Drivers, LocalizationService.Get("Nav.UsbDrivers"), ""),
            new(Routes.History, LocalizationService.Get("Nav.History"), ""),
            new(Routes.Terminal, LocalizationService.Get("Nav.Terminal"), ""),
            new(Routes.Settings, LocalizationService.Get("Nav.Settings"), "")
        ];

        _connectivity.Changed += OnStatusChanged;
        _license.Changed += OnStatusChanged;
        DeviceContext.PropertyChanged += OnContextChanged;
        DeviceContext.Devices.CollectionChanged += OnDevicesChanged;
    }

    public DeviceContext DeviceContext { get; }
    public InfoPanelViewModel Info { get; }
    public IReadOnlyList<ShortcutItem> Shortcuts { get; }
    public int CatalogCount { get; }
    public int ModuleCount => _modules.Modules.Count;
    public int AvailableServiceCount => _modules.Modules.Sum(m => m.Services.Count(s => s.IsAvailableNow));
    public int TotalServiceCount => _modules.Modules.Sum(m => m.Services.Count);

    [ObservableProperty] private bool _needsPlatformTools;
    [ObservableProperty] private string? _adbPath;
    [ObservableProperty] private LicenseState _licenseState;
    [ObservableProperty] private ServiceHealth _internetHealth;
    [ObservableProperty] private string? _internetError;
    [ObservableProperty] private ServiceHealth _serverHealth;
    [ObservableProperty] private int _connectedDevices;

    public Task OnNavigatedToAsync()
    {
        ReadStatus();
        Info.ShowDevice(DeviceContext.Selected);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Open(string? route)
    {
        if (!string.IsNullOrEmpty(route)) _navigation.Navigate(route);
    }

    [RelayCommand]
    private void ShowDevice(DeviceInfo? device)
    {
        if (device is null) return;
        DeviceContext.Selected = device;
        Info.ShowDevice(device);
    }

    /// <summary>Opens the brand page that matches the selected device.</summary>
    [RelayCommand]
    private void OpenDeviceBrand()
    {
        var device = DeviceContext.Selected;
        if (device is null) return;
        var route = _modules.ForDevice(device)
            .Select(m => WorkspaceCatalog.RouteForModule(m.Id))
            .FirstOrDefault(r => r is not null && r is not "ws.adb" and not "ws.fastboot");
        if (route is not null) _navigation.Navigate(route);
    }

    [RelayCommand]
    private async Task SelectPlatformToolsAsync()
    {
        var folder = _dialogs.PickFolder(null);
        if (folder is null) return;

        var exe = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        string? toolsDir = File.Exists(Path.Combine(folder, exe)) ? folder
            : File.Exists(Path.Combine(folder, "platform-tools", exe)) ? Path.Combine(folder, "platform-tools")
            : null;

        if (toolsDir is null)
        {
            _dialogs.Error(LocalizationService.Get("Home.NoAdbInFolder"));
            return;
        }

        var updated = _settings.Current.Clone();
        updated.PlatformToolsPath = toolsDir;
        await _settings.SaveAsync(updated);
        _shell.RefreshTools();
        _log.Ok($"Platform-Tools: {toolsDir}");
        ReadStatus();
        await _devices.RefreshAsync();
    }

    [RelayCommand]
    private void DownloadPlatformTools() => _dialogs.OpenUrl(PlatformToolsPage);

    private void ReadStatus()
    {
        var adb = _tools.Locate("adb");
        NeedsPlatformTools = !adb.IsFound;
        AdbPath = adb.Path;
        LicenseState = _license.Current.State;
        InternetHealth = _connectivity.Internet;
        InternetError = _connectivity.LastInternetError;
        ServerHealth = _connectivity.Server;
        ConnectedDevices = DeviceContext.Devices.Count;
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Ui.Post(ReadStatus);

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceContext.Selected)) Info.ShowDevice(DeviceContext.Selected);
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ConnectedDevices = DeviceContext.Devices.Count;
        Info.ShowDevice(DeviceContext.Selected);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connectivity.Changed -= OnStatusChanged;
        _license.Changed -= OnStatusChanged;
        DeviceContext.PropertyChanged -= OnContextChanged;
        DeviceContext.Devices.CollectionChanged -= OnDevicesChanged;
    }
}
