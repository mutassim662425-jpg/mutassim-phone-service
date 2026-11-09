namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Minimal serial transport the AT engine needs. Abstracted so the parsing, sequencing and diagnostic
/// logic can be unit-tested with a fake channel, with no COM port or hardware.
/// </summary>
public interface ISerialChannel : IDisposable
{
    string PortName { get; }
    bool IsOpen { get; }
    void Open();
    void Close();
    /// <summary>Clears any buffered input/output before a new command.</summary>
    void DiscardBuffers();
    void Write(string text);
    /// <summary>Returns whatever text is currently buffered (possibly empty); never blocks for long.</summary>
    string ReadExisting();
}
