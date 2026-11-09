using System.IO.Ports;
using System.Runtime.Versioning;

namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Real COM-port implementation over <see cref="SerialPort"/>. Only this class touches the hardware;
/// everything else in the engine works on <see cref="ISerialChannel"/>. Default line settings match the
/// common phone modem/diagnostic interface (115200 8-N-1); they can be overridden per device.
/// </summary>
public sealed class SystemSerialChannel : ISerialChannel
{
    private readonly SerialPort _port;

    public SystemSerialChannel(string portName, int baudRate = 115200, int readTimeoutMs = 400)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = readTimeoutMs,
            WriteTimeout = 1000,
            NewLine = "\r",
            DtrEnable = true,
            RtsEnable = true,
            Encoding = System.Text.Encoding.ASCII
        };
    }

    public string PortName => _port.PortName;
    public bool IsOpen => _port.IsOpen;

    public void Open()
    {
        if (!_port.IsOpen) _port.Open();
        DiscardBuffers();
    }

    public void Close()
    {
        if (_port.IsOpen) _port.Close();
    }

    public void DiscardBuffers()
    {
        if (!_port.IsOpen) return;
        try { _port.DiscardInBuffer(); _port.DiscardOutBuffer(); } catch (InvalidOperationException) { /* closed mid-call */ }
    }

    public void Write(string text) => _port.Write(text);

    public string ReadExisting()
    {
        try { return _port.ReadExisting(); }
        catch (TimeoutException) { return string.Empty; }
    }

    public void Dispose() => _port.Dispose();

    /// <summary>Lists the COM port names present on this machine (Windows and Linux both supported by SerialPort).</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> AvailablePorts() => SerialPort.GetPortNames();
}
