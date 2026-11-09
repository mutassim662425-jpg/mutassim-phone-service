using MUTASSIM.ToolPro.Serial;

namespace MUTASSIM.ToolPro.Tests.Unit.Serial;

public class SamsungComPortLocatorTests
{
    [Fact]
    public void Picks_the_samsung_modem_port_over_others()
    {
        var ports = new[]
        {
            new SerialPortInfo("COM1", "Communications Port"),
            new SerialPortInfo("COM7", "SAMSUNG Mobile USB Diagnostic", SamsungComPortLocator.SamsungVendorId, 0x6860),
            new SerialPortInfo("COM5", "SAMSUNG Mobile USB Modem", SamsungComPortLocator.SamsungVendorId, 0x6860),
            new SerialPortInfo("COM3", "USB Serial Device", 0x1A86, 0x7523),
        };

        var best = SamsungComPortLocator.PickBest(ports);
        Assert.NotNull(best);
        Assert.Equal("COM5", best!.PortName); // modem outranks diagnostic
    }

    [Fact]
    public void Ranks_modem_then_diagnostic_then_rest()
    {
        var ports = new[]
        {
            new SerialPortInfo("COM1", "Communications Port"),
            new SerialPortInfo("COM7", "SAMSUNG Mobile USB Diagnostic", SamsungComPortLocator.SamsungVendorId),
            new SerialPortInfo("COM5", "SAMSUNG Mobile USB Modem", SamsungComPortLocator.SamsungVendorId),
        };
        var ranked = SamsungComPortLocator.Rank(ports);
        Assert.Equal(new[] { "COM5", "COM7", "COM1" }, ranked.Select(p => p.PortName));
    }

    [Fact]
    public void PickBest_returns_null_when_nothing_looks_samsung()
    {
        var ports = new[] { new SerialPortInfo("COM1", "Communications Port") };
        Assert.Null(SamsungComPortLocator.PickBest(ports));
    }

    [Theory]
    [InlineData("SAMSUNG Mobile USB Modem (COM5)", "COM5")]
    [InlineData("Some Device (com12)", "COM12")]
    [InlineData("No port here", null)]
    public void Extracts_com_port_from_name(string name, string? expected) =>
        Assert.Equal(expected, SamsungComPortLocator.ExtractComPort(name));

    [Fact]
    public void Strips_com_suffix_for_description()
    {
        Assert.Equal("SAMSUNG Mobile USB Modem", SamsungComPortLocator.DescriptionFromName("SAMSUNG Mobile USB Modem (COM5)"));
    }

    [Theory]
    [InlineData(@"USB\VID_04E8&PID_6860\0123456789", 0x04E8, 0x6860)]
    [InlineData(@"USB\VID_1a86&PID_7523", 0x1A86, 0x7523)]
    public void Parses_vid_pid(string pnp, int vid, int pid)
    {
        var r = SamsungComPortLocator.ParseVidPid(pnp);
        Assert.NotNull(r);
        Assert.Equal(vid, r!.Value.Vid);
        Assert.Equal(pid, r.Value.Pid);
    }

    [Fact]
    public void ParseVidPid_returns_null_when_absent() =>
        Assert.Null(SamsungComPortLocator.ParseVidPid("ACPI\\PNP0501"));
}
