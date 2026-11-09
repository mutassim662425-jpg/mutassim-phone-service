using MUTASSIM.ToolPro.Serial;

namespace MUTASSIM.ToolPro.App.Serial;

/// <summary>Creates real COM-port channels. The only place the UI binds the engine to physical hardware.</summary>
public sealed class SystemSerialChannelFactory : ISerialChannelFactory
{
    public ISerialChannel Create(string portName, int baudRate = 115200) => new SystemSerialChannel(portName, baudRate);
}
