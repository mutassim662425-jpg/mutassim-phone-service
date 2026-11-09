using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.Core.Modules;

/// <summary>
/// A pluggable functional module (a brand, a chipset engine, a protocol, or a system service).
/// Adding a brand = adding one class implementing this interface (in-tree or as a plugin).
/// </summary>
public interface IServiceModule
{
    string Id { get; }
    string DisplayName { get; }
    ModuleCategory Category { get; }
    string Description { get; }
    IReadOnlyList<string> Brands { get; }
    IReadOnlyList<ServiceDescriptor> Services { get; }

    /// <summary>
    /// True only when the device is positively identified as belonging to this module
    /// (by reported manufacturer/brand or USB vendor id). Never true by default.
    /// </summary>
    bool CanHandle(DeviceInfo device);
}

public interface IModuleRegistry
{
    IReadOnlyList<IServiceModule> Modules { get; }
    void Register(IServiceModule module);
    IServiceModule? Find(string id);
    IReadOnlyList<IServiceModule> ForDevice(DeviceInfo device);
    event EventHandler<IServiceModule>? ModuleRegistered;
}

/// <summary>Convenience base class: brand match by name or USB vendor id.</summary>
public abstract class ServiceModuleBase : IServiceModule
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract ModuleCategory Category { get; }
    public abstract string Description { get; }
    public virtual IReadOnlyList<string> Brands => [];
    public virtual IReadOnlyList<ushort> UsbVendorIds => [];
    public abstract IReadOnlyList<ServiceDescriptor> Services { get; }

    public virtual bool CanHandle(DeviceInfo device)
    {
        if (Brands.Count > 0)
        {
            foreach (var brand in Brands)
            {
                if (Matches(device.Manufacturer, brand) || Matches(device.Brand, brand))
                    return true;
            }
        }

        return device.UsbVendorId is { } vid && UsbVendorIds.Contains(vid);
    }

    private static bool Matches(string? value, string brand) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Equals(brand, StringComparison.OrdinalIgnoreCase);
}
