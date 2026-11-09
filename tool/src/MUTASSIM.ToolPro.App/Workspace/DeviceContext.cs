using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.App.Workspace;

/// <summary>
/// The device every operation acts on (bottom bar selector). A newly connected device is selected
/// automatically; if the selected device disconnects, the selection moves to another connected device or none.
/// Operations always target <see cref="Selected"/> by its key, so two devices are never mixed.
/// </summary>
public sealed partial class DeviceContext : ObservableObject
{
    public DeviceContext(IDeviceManager manager, OperationLog log)
    {
        foreach (var d in manager.Devices) Devices.Add(d);
        Selected = Devices.LastOrDefault();

        manager.DeviceChanged += (_, e) => Ui.Post(() =>
        {
            var index = IndexOf(e.Device.Key);
            switch (e.Kind)
            {
                case DeviceChangeKind.Connected:
                    if (index < 0) Devices.Add(e.Device);
                    else Devices[index] = e.Device;
                    Selected = e.Device;
                    log.Ok($"Connected: {Describe(e.Device)}");
                    break;

                case DeviceChangeKind.Updated:
                    if (index >= 0)
                    {
                        var wasSelected = Selected?.Key == e.Device.Key;
                        Devices[index] = e.Device;
                        if (wasSelected) Selected = e.Device;
                    }
                    break;

                case DeviceChangeKind.Disconnected:
                    if (index >= 0)
                    {
                        var wasSelected = Selected?.Key == e.Device.Key;
                        Devices.RemoveAt(index);
                        if (wasSelected) Selected = Devices.LastOrDefault();
                    }
                    log.Warn($"Disconnected: {Describe(e.Device)}");
                    break;
            }
        });
    }

    public ObservableCollection<DeviceInfo> Devices { get; } = [];

    [ObservableProperty] private DeviceInfo? _selected;

    public static string Describe(DeviceInfo d) =>
        $"{d.DisplayManufacturer} {d.DisplayName} [{d.Mode.ToDisplayText()}]" +
        (d.SerialNumber is { } s ? $" SN:{s}" : "");

    private int IndexOf(string key)
    {
        for (var i = 0; i < Devices.Count; i++)
            if (Devices[i].Key == key) return i;
        return -1;
    }
}
