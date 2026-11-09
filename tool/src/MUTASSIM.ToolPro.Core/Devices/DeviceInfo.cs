namespace MUTASSIM.ToolPro.Core.Devices;

/// <summary>
/// Snapshot of one physically connected device as reported by a detector.
/// Every field is nullable: a value is only present when the device/interface actually reported it.
/// Never fill a field with a guessed value.
/// </summary>
public sealed record DeviceInfo
{
    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>();

    /// <summary>
    /// Unique key of the connection (e.g. "adb:R58N12ABC" or "usb:USB\VID_04E8&amp;PID_685D\..."),
    /// used to keep data of different devices strictly separated.
    /// </summary>
    public required string Key { get; init; }

    public required ConnectionMode Mode { get; init; }

    /// <summary>Name of the detector that produced this snapshot.</summary>
    public required string Source { get; init; }

    public string? Manufacturer { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? ModelNumber { get; init; }
    public string? Product { get; init; }
    public string? BuildNumber { get; init; }
    public string? AndroidVersion { get; init; }
    public string? SecurityPatch { get; init; }
    public string? KernelVersion { get; init; }
    public string? Chipset { get; init; }
    public string? CpuAbi { get; init; }
    public string? StorageSize { get; init; }
    public string? BootloaderState { get; init; }
    public string? SerialNumber { get; init; }

    public ushort? UsbVendorId { get; init; }
    public ushort? UsbProductId { get; init; }
    public string? UsbInstanceId { get; init; }
    public string? ComPort { get; init; }
    public DriverState DriverState { get; init; } = DriverState.Unknown;

    /// <summary>Raw key/value data returned by the device (e.g. getprop / getvar output).</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = EmptyProperties;

    public DateTimeOffset FirstSeen { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.Now;

    public string DisplayName =>
        FirstNonEmpty(Model, Product, ModelNumber) ?? ConnectionModeExtensions.ToDisplayText(Mode);

    public string DisplayManufacturer => FirstNonEmpty(Manufacturer, Brand) ?? "—";

    public string? UsbIdText =>
        UsbVendorId is { } vid && UsbProductId is { } pid ? $"{vid:X4}:{pid:X4}" : null;

    /// <summary>
    /// True when the user-visible state is identical (ignores timestamps and raw property bag identity).
    /// Used by the device manager to decide whether to raise an "Updated" event.
    /// </summary>
    public bool HasSameStateAs(DeviceInfo other) =>
        Key == other.Key && Mode == other.Mode && Manufacturer == other.Manufacturer &&
        Brand == other.Brand && Model == other.Model && ModelNumber == other.ModelNumber &&
        Product == other.Product && BuildNumber == other.BuildNumber &&
        AndroidVersion == other.AndroidVersion && SecurityPatch == other.SecurityPatch &&
        BootloaderState == other.BootloaderState && SerialNumber == other.SerialNumber &&
        UsbVendorId == other.UsbVendorId && UsbProductId == other.UsbProductId &&
        ComPort == other.ComPort && DriverState == other.DriverState &&
        Properties.Count == other.Properties.Count;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
