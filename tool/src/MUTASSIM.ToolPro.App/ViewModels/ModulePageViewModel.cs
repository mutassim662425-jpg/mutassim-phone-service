using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed class ServiceRowViewModel(ServiceDescriptor descriptor)
{
    public ServiceDescriptor Descriptor { get; } = descriptor;
    public string Name => Descriptor.Name;
    public ImplementationStatus Status => Descriptor.Status;
    public string? Notes => Descriptor.Notes;
    public bool IsDestructive => Descriptor.IsDestructive;
    public string Modes => Descriptor.RequiredModes.Count == 0
        ? "—"
        : string.Join(", ", Descriptor.RequiredModes.Select(m => m.ToDisplayText()));
    public string Phase => Descriptor.PlannedPhase > 0
        ? LocalizationService.Format("Module.PhaseN", Descriptor.PlannedPhase)
        : "—";
}

public sealed partial class ModuleSectionViewModel : ObservableObject
{
    public ModuleSectionViewModel(IServiceModule module)
    {
        Module = module;
        Services = module.Services.Select(s => new ServiceRowViewModel(s)).ToArray();
        Brands = module.Brands.Count > 0 ? string.Join(" · ", module.Brands.Select(b => b.ToUpperInvariant())) : null;
        AvailableCount = module.Services.Count(s => s.IsAvailableNow);
    }

    public IServiceModule Module { get; }
    public string DisplayName => Module.DisplayName;
    public string Description => Module.Description;
    public string CategoryText => LocalizationService.ForEnum(Module.Category);
    public string? Brands { get; }
    public IReadOnlyList<ServiceRowViewModel> Services { get; }
    public int AvailableCount { get; }
    public string Summary => LocalizationService.Format("Module.Summary", AvailableCount, Services.Count);

    public ObservableCollection<string> ConnectedDevices { get; } = [];
}

/// <summary>
/// Generic page for a brand / chipset / protocol / service module. Shows each service with its REAL
/// implementation status. No action button is shown for a service that is not implemented.
/// </summary>
public sealed partial class ModulePageViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceManager _devices;

    public ModulePageViewModel(IModuleRegistry registry, IDeviceManager devices, string[] moduleIds, string titleKey)
    {
        _devices = devices;
        Title = LocalizationService.Get(titleKey);
        Sections = moduleIds
            .Select(registry.Find)
            .Where(m => m is not null)
            .Select(m => new ModuleSectionViewModel(m!))
            .ToArray();
        MissingModule = Sections.Count != moduleIds.Length;

        _devices.DeviceChanged += OnDeviceChanged;
        RefreshDevices();
    }

    public string Title { get; }
    public IReadOnlyList<ModuleSectionViewModel> Sections { get; }
    public bool MissingModule { get; }

    private void OnDeviceChanged(object? sender, DeviceChangedEventArgs e) => Ui.Post(RefreshDevices);

    private void RefreshDevices()
    {
        var connected = _devices.Devices;
        foreach (var section in Sections)
        {
            section.ConnectedDevices.Clear();
            foreach (var device in connected.Where(section.Module.CanHandle))
                section.ConnectedDevices.Add($"{device.DisplayManufacturer} {device.DisplayName} — {device.Mode.ToDisplayText()}");
        }
    }

    public void Dispose() => _devices.DeviceChanged -= OnDeviceChanged;
}
