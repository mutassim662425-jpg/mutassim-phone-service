namespace MUTASSIM.ToolPro.Core.Modules;

/// <summary>Well-known ids of the built-in modules (used by navigation).</summary>
public static class ModuleIds
{
    public const string Samsung = "samsung";
    public const string Xiaomi = "xiaomi";
    public const string Huawei = "huawei";
    public const string Honor = "honor";
    public const string Oppo = "oppo";
    public const string Realme = "realme";
    public const string Vivo = "vivo";
    public const string Tecno = "tecno";
    public const string Infinix = "infinix";
    public const string Motorola = "motorola";
    public const string Nokia = "nokia";
    public const string GooglePixel = "pixel";
    public const string OnePlus = "oneplus";
    public const string Zte = "zte";
    public const string Lenovo = "lenovo";
    public const string Sony = "sony";
    public const string Lg = "lg";
    public const string Apple = "apple";
    public const string Qualcomm = "qualcomm";
    public const string MediaTek = "mediatek";
    public const string Adb = "adb";
    public const string Fastboot = "fastboot";
    public const string Frp = "frp";
    public const string Mdm = "mdm";
    public const string Firmware = "firmware";
    public const string UsbDrivers = "usb-drivers";
    public const string ApiServices = "api-services";
}

/// <summary>Short factory helpers to keep module declarations readable.</summary>
public static class Svc
{
    public static ServiceDescriptor Planned(string id, string name, int phase, string? notes = null) =>
        new(id, name, ImplementationStatus.InDevelopment, phase, notes);

    public static ServiceDescriptor Authorized(string id, string name, int phase, string notes) =>
        new(id, name, ImplementationStatus.RequiresExternalAuthorization, phase, notes);

    public static ServiceDescriptor External(string id, string name, int phase, string notes) =>
        new(id, name, ImplementationStatus.RequiresAdditionalProtocolOrHardware, phase, notes);

    public static ServiceDescriptor Unsupported(string id, string name, string notes) =>
        new(id, name, ImplementationStatus.NotSupported, 0, notes);

    public static ServiceDescriptor Built(string id, string name, int phase, string notes) =>
        new(id, name, ImplementationStatus.CompletedNeedsDeviceTest, phase, notes);
}
