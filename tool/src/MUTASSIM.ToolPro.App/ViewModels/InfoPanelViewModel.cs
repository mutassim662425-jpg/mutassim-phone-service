using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Infrastructure.Catalog;

namespace MUTASSIM.ToolPro.App.ViewModels;

/// <summary>
/// The device information card (image + name + spec rows with copy buttons). Shows either the connected
/// device (live values only) or a catalog model (reference values, clearly labelled as such).
/// </summary>
public sealed partial class InfoPanelViewModel(
    DeviceCatalog catalog,
    ImageLibrary images,
    IModuleRegistry modules,
    IDialogService dialogs) : ObservableObject
{
    public ObservableCollection<InfoRow> Rows { get; } = [];

    [ObservableProperty] private bool _hasInfo;
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _subtitle;
    [ObservableProperty] private string _badge = "";
    [ObservableProperty] private string? _note;
    [ObservableProperty] private ImageSource? _image;
    [ObservableProperty] private Brush _accent = BrandStyle.Accent(null);

    public void Clear()
    {
        Rows.Clear();
        HasInfo = false;
        Title = LocalizationService.Get("Info.Empty");
        Subtitle = null;
        Badge = "";
        Note = LocalizationService.Get("Info.EmptyHint");
        Image = null;
        Accent = BrandStyle.Accent(null);
    }

    public void ShowDevice(DeviceInfo? d)
    {
        if (d is null)
        {
            Clear();
            return;
        }

        var match = catalog.FindForDevice(d);
        var route = modules.ForDevice(d).Select(m => WorkspaceCatalog.RouteForModule(m.Id)).FirstOrDefault(r => r is not null)
                    ?? match?.Brand;

        Rows.Clear();
        Add("Info.Brand", d.Brand is { Length: > 0 } b && !b.Equals(d.Manufacturer, StringComparison.OrdinalIgnoreCase)
            ? $"{d.Manufacturer} / {b}" : d.Manufacturer ?? d.Brand);
        Add("Info.Model", d.Model);
        Add("Device.Model", d.ModelNumber);
        Add("Info.Product", d.Product);
        Add("Device.Chipset", d.Chipset ?? match?.Chipset);
        Add("Device.Android", d.AndroidVersion is { } v ? $"Android {v}" : null);
        Add("Device.Patch", d.SecurityPatch);
        Add("Device.Build", d.BuildNumber);
        Add("Device.Bootloader", d.BootloaderState);
        Add("Device.Serial", d.SerialNumber);
        Add("Info.Mode", d.Mode.ToDisplayText());
        Add("Device.UsbId", d.UsbIdText);
        Add("Info.Driver", d.DriverState == DriverState.Unknown ? null : LocalizationService.ForEnum(d.DriverState));
        Add("Device.Com", d.ComPort);
        Add("Info.Match", match?.Name);

        HasInfo = true;
        IsLive = true;
        Title = $"{d.DisplayManufacturer} {d.DisplayName}".Trim();
        Subtitle = match?.Subtitle;
        Badge = LocalizationService.Get("Info.Live");
        Note = d.Mode is ConnectionMode.Mtp or ConnectionMode.Unknown ? LocalizationService.Get("Run.EnableAdbHint") : null;
        Image = images.Device(match, d);
        Accent = BrandStyle.Accent(route);
    }

    public void ShowModel(CatalogModel m)
    {
        Rows.Clear();
        Add("Info.Series", m.Series);
        Add("Device.Model", m.Codename);
        Add("Info.ModelCode", m.ModelCode);
        Add("Device.Chipset", m.Chipset);
        Add("Info.ChipFamily", m.Family == ChipsetFamily.Unknown ? null : m.Family.ToString());

        HasInfo = true;
        IsLive = false;
        Title = m.Name;
        Subtitle = m.Subtitle;
        Badge = LocalizationService.Get("Info.Catalog");
        Note = LocalizationService.Get("Info.CatalogNote");
        Image = images.Device(m);
        Accent = BrandStyle.Accent(m.Brand);
    }

    [RelayCommand]
    private void Copy(string? value)
    {
        if (!string.IsNullOrEmpty(value)) dialogs.CopyToClipboard(value);
    }

    private void Add(string labelKey, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Rows.Add(new InfoRow(LocalizationService.Get(labelKey), value.Trim()));
    }
}
