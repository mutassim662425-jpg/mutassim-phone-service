using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Core;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed partial class NavItemViewModel(NavEntry entry) : ObservableObject
{
    public NavEntry Entry { get; } = entry;
    public string Route => Entry.Route;
    public string Glyph => Entry.Glyph;
    [ObservableProperty] private string _title = TitleOf(entry);
    [ObservableProperty] private bool _isSelected;

    public void RefreshText() => Title = TitleOf(Entry);

    private static string TitleOf(NavEntry e) =>
        e.Workspace?.Tab ?? (e.TitleKey is { } k ? LocalizationService.Get(k) : e.Route);
}

/// <summary>Coloured button in the brand bar (plain name, or an owner-supplied logo image).</summary>
public sealed partial class BrandButtonViewModel : ObservableObject
{
    private readonly Func<string> _title;

    public BrandButtonViewModel(string route, Func<string> title, System.Windows.Media.Brush accent,
        System.Windows.Media.ImageSource? logo, string? glyph = null)
    {
        Route = route;
        _title = title;
        Accent = accent;
        Logo = logo;
        Glyph = glyph;
        TitleText = title().ToUpperInvariant();
    }

    public string Route { get; }
    public System.Windows.Media.Brush Accent { get; }
    public System.Windows.Media.ImageSource? Logo { get; }
    public bool HasLogo => Logo is not null;
    public string? Glyph { get; }
    public bool HasGlyph => Glyph is not null;
    [ObservableProperty] private string _titleText = "";
    [ObservableProperty] private bool _isSelected;

    public string Title => TitleText;

    public void RefreshText()
    {
        TitleText = _title().ToUpperInvariant();
        OnPropertyChanged(nameof(Title));
    }
}

/// <summary>Shell: title bar, tabs, operation log, device bar and status bar — all bound to real services.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDeviceManager _deviceManager;
    private readonly IConnectivityService _connectivity;
    private readonly ILicenseService _license;
    private readonly IUpdateService _updates;
    private readonly IExternalToolLocator _tools;
    private readonly IOperationTracker _operations;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DispatcherTimer _connectivityTimer;

    public MainViewModel(
        NavigationService navigation,
        LocalizationService localization,
        DeviceContext deviceContext,
        OperationRunner runner,
        OperationLog log,
        IDeviceManager deviceManager,
        IConnectivityService connectivity,
        ILicenseService license,
        IUpdateService updates,
        IExternalToolLocator tools,
        IOperationTracker operations,
        IDialogService dialogs,
        ImageLibrary images,
        AccountSession account,
        ILogger<MainViewModel> logger)
    {
        Navigation = navigation;
        Localization = localization;
        DeviceContext = deviceContext;
        Account = account;
        Runner = runner;
        Log = log;
        _deviceManager = deviceManager;
        _connectivity = connectivity;
        _license = license;
        _updates = updates;
        _tools = tools;
        _operations = operations;
        _dialogs = dialogs;
        _logger = logger;

        Tabs = new ObservableCollection<NavItemViewModel>(Routes.Tabs.Select(t => new NavItemViewModel(t)));
        ToolItems = new ObservableCollection<NavItemViewModel>(Routes.Tools.Select(t => new NavItemViewModel(t)));
        // Brand bar: every workspace, then the Samsung Flash page (the HOME button was removed on request).
        var flashBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0B, 0x2E, 0x8A));
        flashBrush.Freeze();
        var flash = new BrandButtonViewModel(Routes.SamsungFlash, () => LocalizationService.Get("Nav.SamFlash"), flashBrush, null, "\uE945");
        BrandButtons = new ObservableCollection<BrandButtonViewModel>(
            WorkspaceCatalog.All.Select(w =>
                new BrandButtonViewModel(w.Route, () => w.Tab, BrandStyle.Accent(w.Route), images.Brand(w.Route)))
            .Append(flash));
        Routes.RegisterAll(navigation);
        navigation.Navigated += (_, route) => SyncSelection(route);

        localization.LanguageChanged += (_, _) => OnLanguageChanged();
        deviceManager.DeviceChanged += (_, _) => Ui.Post(RefreshDeviceStatus);
        deviceManager.ScanCompleted += (_, _) => Ui.Post(RefreshDeviceStatus);
        connectivity.Changed += (_, _) => Ui.Post(RefreshConnectivity);
        license.Changed += (_, _) => Ui.Post(ReadLicense);
        updates.Changed += (_, _) => Ui.Post(() => UpdateState = _updates.LastResult.State);
        operations.OperationFinished += (_, record) => Ui.Post(() => SetLastOperation(record));

        _connectivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _connectivityTimer.Tick += async (_, _) => await SafeAsync(() => _connectivity.RefreshAsync());
    }

    public NavigationService Navigation { get; }
    public LocalizationService Localization { get; }
    public AccountSession Account { get; }
    public DeviceContext DeviceContext { get; }
    public OperationRunner Runner { get; }
    public OperationLog Log { get; }
    public ObservableCollection<NavItemViewModel> Tabs { get; }
    public ObservableCollection<NavItemViewModel> ToolItems { get; }
    public ObservableCollection<BrandButtonViewModel> BrandButtons { get; }
    public string Copyright => $"© {DateTime.Now.Year} {AppInfo.Developer}. All rights reserved.";

    // Connection indicators (count of connected devices per mode; "-" when none)
    [ObservableProperty] private string _connAdb = "-";
    [ObservableProperty] private string _connFastboot = "-";
    [ObservableProperty] private string _connEdl = "-";
    [ObservableProperty] private string _connBrom = "-";
    [ObservableProperty] private string? _expiresText;

    public string ProductName => AppInfo.ProductName;
    public string DisplayVersion => AppInfo.DisplayVersion;
    public string UserName => Environment.UserName;

    [ObservableProperty] private LicenseState _licenseState = LicenseState.NotConfigured;
    [ObservableProperty] private ServiceHealth _internetHealth = ServiceHealth.Unknown;
    [ObservableProperty] private ServiceHealth _serverHealth = ServiceHealth.Unknown;
    [ObservableProperty] private UpdateState _updateState = UpdateState.NotChecked;

    [ObservableProperty] private bool _adbFound;
    [ObservableProperty] private bool _fastbootFound;
    [ObservableProperty] private string _adbStatusText = "";
    [ObservableProperty] private string _fastbootStatusText = "";
    [ObservableProperty] private string _usbStatusText = "";
    [ObservableProperty] private bool _usbMonitorActive;
    [ObservableProperty] private int _connectedDevices;
    [ObservableProperty] private string _lastOperationText = "";

    public async Task InitializeAsync()
    {
        Navigation.Navigate(Routes.Start);
        RefreshTools();
        RefreshDeviceStatus();
        SetLastOperation(null);
        Log.Info($"{AppInfo.ProductName} {AppInfo.DisplayVersion} — {LocalizationService.Get("Log.Ready")}");
        if (!AdbFound) Log.Warn(LocalizationService.Get("Log.AdbMissing"));

        await SafeAsync(() => _license.RefreshAsync());
        ReadLicense();
        await _deviceManager.StartAsync();
        RefreshDeviceStatus();
        _connectivityTimer.Start();
        await SafeAsync(() => _connectivity.RefreshAsync());
    }

    [RelayCommand]
    private void Navigate(NavItemViewModel? item)
    {
        if (item is not null) Navigation.Navigate(item.Route);
    }

    [RelayCommand]
    private void NavigateRoute(string? route)
    {
        if (!string.IsNullOrEmpty(route)) Navigation.Navigate(route);
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        var result = await _updates.CheckAsync();
        var message = result.State switch
        {
            UpdateState.UpdateAvailable => LocalizationService.Format("Update.Available", result.Latest, result.Current.ToString(3)),
            UpdateState.UpToDate => LocalizationService.Format("Update.UpToDate", result.Current.ToString(3)),
            UpdateState.NotConfigured => LocalizationService.Get("Update.NotConfiguredMessage"),
            _ => LocalizationService.Format("Update.Failed", result.Message)
        };
        if (result.State == UpdateState.CheckFailed) _dialogs.Error(message);
        else _dialogs.Info(message);
    }

    [RelayCommand]
    private void CopyLog() => _dialogs.CopyToClipboard(Log.ToText());

    [RelayCommand]
    private async Task SaveLogAsync()
    {
        var path = _dialogs.SaveFile("Log (*.txt)|*.txt", $"MUTASSIM_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        if (path is not null) await File.WriteAllTextAsync(path, Log.ToText(), new UTF8Encoding(true));
    }

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    [RelayCommand]
    private async Task RescanAsync()
    {
        Log.Info(LocalizationService.Get("Log.Scanning"));
        await SafeAsync(() => _deviceManager.RefreshAsync());
        Log.Info(LocalizationService.Format("Log.ScanResult", _deviceManager.Devices.Count));
    }

    /// <summary>Re-read tool locations (after settings change).</summary>
    public void RefreshTools()
    {
        var adb = _tools.Locate("adb");
        var fastboot = _tools.Locate("fastboot");
        AdbFound = adb.IsFound;
        FastbootFound = fastboot.IsFound;
        AdbStatusText = LocalizationService.Get(adb.IsFound ? "Status.ToolFound" : "Status.ToolMissing");
        FastbootStatusText = LocalizationService.Get(fastboot.IsFound ? "Status.ToolFound" : "Status.ToolMissing");
    }

    private void ReadLicense()
    {
        LicenseState = _license.Current.State;
        ExpiresText = _license.Current.ExpiresAt is { } exp ? exp.ToString("yyyy-MM-dd") : null;
    }

    private void RefreshDeviceStatus()
    {
        var devices = _deviceManager.Devices;
        ConnectedDevices = devices.Count;
        static string Count(int n) => n == 0 ? "-" : n.ToString();
        ConnAdb = Count(devices.Count(d => d.Mode is ConnectionMode.Adb or ConnectionMode.Recovery or ConnectionMode.Sideload
            or ConnectionMode.AdbUnauthorized or ConnectionMode.AdbOffline));
        ConnFastboot = Count(devices.Count(d => d.Mode is ConnectionMode.Fastboot or ConnectionMode.FastbootD));
        ConnEdl = Count(devices.Count(d => d.Mode is ConnectionMode.QualcommEdl));
        ConnBrom = Count(devices.Count(d => d.Mode is ConnectionMode.MediaTekBrom or ConnectionMode.MediaTekPreloader));
        var detectors = _deviceManager.Detectors;
        UsbMonitorActive = _deviceManager.IsRunning && detectors.Any(d => d.IsAvailable);
        UsbStatusText = detectors.Count == 0
            ? LocalizationService.Get("Status.NoDetectors")
            : LocalizationService.Format("Status.DetectorsActive", detectors.Count(d => d.IsAvailable), detectors.Count);
    }

    private void RefreshConnectivity()
    {
        InternetHealth = _connectivity.Internet;
        ServerHealth = _connectivity.Server;
    }

    private void SetLastOperation(OperationRecord? record)
    {
        record ??= _operations.LastOperation;
        LastOperationText = record is null
            ? LocalizationService.Get("Status.NoOperations")
            : $"{record.ServiceName} — {LocalizationService.ForEnum(record.Status)} ({record.FinishedAt ?? record.StartedAt:HH:mm:ss})";
    }

    private void OnLanguageChanged()
    {
        foreach (var t in Tabs) t.RefreshText();
        foreach (var t in ToolItems) t.RefreshText();
        foreach (var b in BrandButtons) b.RefreshText();
        RefreshTools();
        RefreshDeviceStatus();
        SetLastOperation(null);
        OnPropertyChanged(nameof(LicenseState));
        OnPropertyChanged(nameof(InternetHealth));
        OnPropertyChanged(nameof(ServerHealth));
        OnPropertyChanged(nameof(UpdateState));
        Navigation.Reload();
    }

    private void SyncSelection(string route)
    {
        foreach (var item in Tabs) item.IsSelected = item.Route == route;
        foreach (var item in ToolItems) item.IsSelected = item.Route == route;
        foreach (var item in BrandButtons) item.IsSelected = item.Route == route;
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh failed");
        }
    }
}
