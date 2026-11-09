using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed record InfoRow(string Label, string Value);

public sealed class DeviceCardViewModel
{
    public DeviceCardViewModel(DeviceInfo device, IReadOnlyList<IServiceModule> modules)
    {
        Device = device;
        Modules = modules;
        var rows = new List<InfoRow>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add(new InfoRow(LocalizationService.Get(key), value));
        }

        Add("Device.Model", device.ModelNumber);
        Add("Device.Android", device.AndroidVersion);
        Add("Device.Patch", device.SecurityPatch);
        Add("Device.Build", device.BuildNumber);
        Add("Device.Chipset", device.Chipset);
        Add("Device.Abi", device.CpuAbi);
        Add("Device.Bootloader", device.BootloaderState);
        Add("Device.Serial", device.SerialNumber);
        Add("Device.UsbId", device.UsbIdText);
        Add("Device.Com", device.ComPort);
        Add("Device.Source", device.Source);
        Rows = rows;
    }

    public DeviceInfo Device { get; }
    public IReadOnlyList<IServiceModule> Modules { get; }
    public IReadOnlyList<InfoRow> Rows { get; }
    public string Manufacturer => Device.DisplayManufacturer;
    public string Name => Device.DisplayName;
    public ConnectionMode Mode => Device.Mode;
    public string ModeText => Device.Mode.ToDisplayText();
    public string ModulesText => Modules.Count == 0
        ? LocalizationService.Get("Device.NoModule")
        : string.Join(", ", Modules.Select(m => m.DisplayName));
    public bool HasModule => Modules.Count > 0;
}

public sealed partial class DeviceManagerViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IDeviceManager _manager;
    private readonly IModuleRegistry _modules;
    private readonly NavigationService _navigation;

    public DeviceManagerViewModel(IDeviceManager manager, IModuleRegistry modules, NavigationService navigation)
    {
        _manager = manager;
        _modules = modules;
        _navigation = navigation;
        _manager.DeviceChanged += OnChanged;
        _manager.ScanCompleted += OnScanCompleted;
    }

    public ObservableCollection<DeviceCardViewModel> Devices { get; } = [];
    public ObservableCollection<DetectorStatus> Detectors { get; } = [];

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasDetectors;
    [ObservableProperty] private bool _isRunning;

    public Task OnNavigatedToAsync()
    {
        Rebuild();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await _manager.RefreshAsync();
        }
        finally
        {
            IsRefreshing = false;
            Rebuild();
        }
    }

    [RelayCommand]
    private void OpenServices(DeviceCardViewModel? card)
    {
        var module = card?.Modules.FirstOrDefault();
        if (module is null) return;
        if (Routes.RouteForModule(module.Id) is { } route) _navigation.Navigate(route);
    }

    private void Rebuild()
    {
        Devices.Clear();
        foreach (var device in _manager.Devices)
            Devices.Add(new DeviceCardViewModel(device, _modules.ForDevice(device)));

        Detectors.Clear();
        foreach (var d in _manager.Detectors) Detectors.Add(d);
        HasDetectors = Detectors.Count > 0;
        IsRunning = _manager.IsRunning;
    }

    private void OnChanged(object? sender, DeviceChangedEventArgs e) => Ui.Post(Rebuild);

    private void OnScanCompleted(object? sender, EventArgs e) => Ui.Post(() =>
    {
        Detectors.Clear();
        foreach (var d in _manager.Detectors) Detectors.Add(d);
        IsRunning = _manager.IsRunning;
    });

    public void Dispose()
    {
        _manager.DeviceChanged -= OnChanged;
        _manager.ScanCompleted -= OnScanCompleted;
    }
}
