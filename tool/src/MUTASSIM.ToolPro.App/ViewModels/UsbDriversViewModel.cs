using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Usb;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed record UsbEntryRow(string VidPid, string Vendor, string Name, string? ComPort, string Mode, DriverState Driver, int ErrorCode, string InstanceId);

/// <summary>USB Drivers: real Windows PnP list of phone/chipset USB devices with their driver state.</summary>
public sealed partial class UsbDriversViewModel(UsbDeviceDetector usb) : ObservableObject, IPageViewModel
{
    public ObservableCollection<UsbEntryRow> Entries { get; } = [];
    [ObservableProperty] private int _missingCount;

    public Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var list = await Task.Run(usb.ListVendorEntries);
        Entries.Clear();
        foreach (var e in list)
        {
            UsbIdClassifier.TryParseInstanceId(e.InstanceId, out var vid, out var pid);
            var c = UsbIdClassifier.Classify(vid, pid);
            Entries.Add(new UsbEntryRow(
                $"{vid:X4}:{pid:X4}",
                c?.VendorName ?? "—",
                e.Name ?? "—",
                UsbDeviceDetector.ComPort(e.Name),
                c is { Mode: not ConnectionMode.Unknown } ? c.Mode.ToDisplayText() : "—",
                e.ErrorCode switch { 0 => DriverState.Installed, 28 => DriverState.Missing, _ => DriverState.Problem },
                e.ErrorCode,
                e.InstanceId));
        }
        MissingCount = Entries.Count(x => x.Driver != DriverState.Installed);
    }
}
