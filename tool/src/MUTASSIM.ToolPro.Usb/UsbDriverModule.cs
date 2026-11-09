using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Usb;

public sealed class UsbDriverModule : ServiceModuleBase
{
    public override string Id => ModuleIds.UsbDrivers;
    public override string DisplayName => "USB Drivers";
    public override ModuleCategory Category => ModuleCategory.System;
    public override string Description =>
        "USB device enumeration (Windows PnP/WMI) and driver diagnostics. Only signed drivers from official vendor sources are recommended; unsigned drivers trigger a clear warning.";

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("usb.classify", "VID/PID classification (known vendor & mode IDs)", 1, "Lookup table; unit tests included"),
        Built("usb.enumerate", "Live USB device enumeration & hot-plug events", 2, "Implemented; awaiting real-device test"),
        Built("usb.unknown", "Detect unknown / driver-less devices", 2, "Implemented; awaiting real-device test"),
        Built("usb.driverstate", "Installed / missing driver status", 2, "Implemented; awaiting real-device test"),
        Built("usb.com", "COM port mapping for serial devices", 2, "Implemented; awaiting real-device test"),
        Planned("usb.links", "Official driver sources per vendor", 5),
        Built("usb.rescan", "Rescan after driver installation", 2, "Implemented; awaiting real-device test")
    ];

    public override bool CanHandle(DeviceInfo device) => device.UsbVendorId is not null;
}
