namespace MUTASSIM.ToolPro.Serial;

/// <summary>A COM port with whatever descriptive metadata the OS could provide.</summary>
public sealed record SerialPortInfo(string PortName, string? Description = null, int? VendorId = null, int? ProductId = null)
{
    /// <summary>"COM5 — SAMSUNG Mobile USB Modem" style label for the UI.</summary>
    public string Display => string.IsNullOrWhiteSpace(Description) ? PortName : $"{PortName} — {Description}";
}

/// <summary>How a port enumerator reports the COM ports present. Abstracted so the UI and tests don't need WMI.</summary>
public interface ISerialPortEnumerator
{
    /// <summary>Lists the COM ports currently present (empty on platforms without serial enumeration).</summary>
    IReadOnlyList<SerialPortInfo> List();
}

/// <summary>Creates an <see cref="ISerialChannel"/> for a port. Abstracted so view-models can be built without hardware.</summary>
public interface ISerialChannelFactory
{
    ISerialChannel Create(string portName, int baudRate = 115200);
}
