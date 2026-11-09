using MUTASSIM.ToolPro.Serial;

namespace MUTASSIM.ToolPro.Tests.Unit.Serial;

/// <summary>A scripted serial channel: maps each written command to a canned device response.</summary>
internal sealed class FakeSerialChannel(Dictionary<string, string> responses) : ISerialChannel
{
    private readonly Queue<string> _pending = new();
    public List<string> Written { get; } = [];
    public string PortName => "FAKE1";
    public bool IsOpen { get; private set; }
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void DiscardBuffers() => _pending.Clear();

    public void Write(string text)
    {
        Written.Add(text);
        var cmd = text.Trim();
        _pending.Enqueue(responses.TryGetValue(cmd, out var r) ? r : "ERROR\r\n");
    }

    public string ReadExisting() => _pending.Count > 0 ? _pending.Dequeue() : string.Empty;
    public void Dispose() { }
}

public class AtResponseParserTests
{
    [Fact]
    public void Ok_response_is_parsed_with_payload()
    {
        var r = AtResponseParser.Parse("AT+CGMI", ["AT+CGMI", "SAMSUNG", "OK"], TimeSpan.Zero);
        Assert.Equal(AtStatus.Ok, r.Status);
        Assert.Equal("SAMSUNG", r.FirstValue);
    }

    [Fact]
    public void Plain_error_is_an_error()
    {
        var r = AtResponseParser.Parse("AT+XYZ", ["ERROR"], TimeSpan.Zero);
        Assert.Equal(AtStatus.Error, r.Status);
    }

    [Theory]
    [InlineData("+CME ERROR: 10", AtStatus.CmeError, 10)]
    [InlineData("+CMS ERROR: 305", AtStatus.CmsError, 305)]
    public void Cme_and_cms_errors_carry_their_code(string finalLine, AtStatus status, int code)
    {
        var r = AtResponseParser.Parse("AT+X", [finalLine], TimeSpan.Zero);
        Assert.Equal(status, r.Status);
        Assert.Equal(code, r.ErrorCode);
    }

    [Fact]
    public void No_final_code_with_lines_is_a_timeout()
    {
        var r = AtResponseParser.Parse("AT", ["some noise"], TimeSpan.FromSeconds(1));
        Assert.Equal(AtStatus.Timeout, r.Status);
    }

    [Fact]
    public void Nothing_received_is_empty()
    {
        var r = AtResponseParser.Parse("AT", [], TimeSpan.FromSeconds(1));
        Assert.Equal(AtStatus.Empty, r.Status);
    }
}

public class AtCommandClassifierTests
{
    [Theory]
    [InlineData("AT+SWATD=0")]
    [InlineData("AT+ACTIVATE=0,0,0")]
    [InlineData("at+frpdel")]
    [InlineData("AT+REMOVEFRP")]
    public void Protection_and_engineering_commands_are_state_change(string text)
    {
        Assert.Equal(AtCommandKind.StateChange, AtCommandClassifier.Classify(text).Kind);
    }

    [Theory]
    [InlineData("ATI")]
    [InlineData("AT+CGSN")]
    [InlineData("AT+CPIN?")]
    [InlineData("AT+CMEE=2")]   // harmless session setting
    public void Reads_queries_and_safe_session_settings_are_read(string text)
    {
        Assert.Equal(AtCommandKind.Read, AtCommandClassifier.Classify(text).Kind);
    }

    [Fact]
    public void Unknown_set_forms_default_to_state_change()
    {
        Assert.Equal(AtCommandKind.StateChange, AtCommandClassifier.Classify("AT+CFUN=1").Kind);
    }
}

public class AtCommandChannelTests
{
    [Fact]
    public async Task Read_command_is_sent_and_parsed()
    {
        var ch = new FakeSerialChannel(new() { ["AT+CGMI"] = "AT+CGMI\r\nSAMSUNG\r\nOK\r\n" });
        var channel = new AtCommandChannel(ch);
        var r = await channel.SendAsync(AtCommandLibrary.Manufacturer);
        Assert.Equal(AtStatus.Ok, r.Status);
        Assert.Equal("SAMSUNG", r.FirstValue);
        Assert.Contains("AT+CGMI\r", ch.Written);
    }

    [Fact]
    public async Task State_change_command_is_refused_and_never_written()
    {
        var ch = new FakeSerialChannel([]);
        var channel = new AtCommandChannel(ch);
        var bad = AtCommand.StateChange("AT+ACTIVATE=0,0,0", "activate");
        await Assert.ThrowsAsync<StateChangeRefusedException>(() => channel.SendAsync(bad));
        Assert.Empty(ch.Written); // nothing sent to the device
    }

    [Fact]
    public async Task Console_refuses_a_typed_protection_command()
    {
        var ch = new FakeSerialChannel([]);
        var channel = new AtCommandChannel(ch);
        await Assert.ThrowsAsync<StateChangeRefusedException>(() => channel.SendConsoleAsync("AT+SWATD=0"));
        Assert.Empty(ch.Written);
    }
}

public class AtSequenceRunnerTests
{
    [Fact]
    public async Task Sequence_with_a_state_change_is_rejected_before_sending()
    {
        var ch = new FakeSerialChannel([]);
        var runner = new AtSequenceRunner(new AtCommandChannel(ch));
        var seq = new AtCommandSequence("mixed",
            [AtCommandLibrary.Attention, AtCommand.StateChange("AT+ACTIVATE=0,0,0", "x")]);
        await Assert.ThrowsAsync<StateChangeRefusedException>(() => runner.RunAsync(seq));
        Assert.Empty(ch.Written);
    }

    [Fact]
    public async Task Read_only_sequence_runs_all_commands()
    {
        var ch = new FakeSerialChannel(new()
        {
            ["AT"] = "OK\r\n",
            ["AT+CGMI"] = "SAMSUNG\r\nOK\r\n"
        });
        var runner = new AtSequenceRunner(new AtCommandChannel(ch));
        var seq = new AtCommandSequence("ids", [AtCommandLibrary.Attention, AtCommandLibrary.Manufacturer]);
        var result = await runner.RunAsync(seq);
        Assert.True(result.AllOk);
        Assert.Equal(2, result.Responses.Count);
    }
}

public class AtDiagnosticReaderTests
{
    [Fact]
    public async Task Reads_manufacturer_model_firmware_and_imei()
    {
        var ch = new FakeSerialChannel(new()
        {
            ["AT+CMEE=2"] = "OK\r\n",
            ["AT"] = "OK\r\n",
            ["AT+CGMI"] = "SAMSUNG\r\nOK\r\n",
            ["AT+CGMM"] = "SM-A515F\r\nOK\r\n",
            ["AT+CGMR"] = "A515FXXU5\r\nOK\r\n",
            ["ATI"] = "Samsung Galaxy A51\r\nOK\r\n",
            ["AT+CGSN"] = "351234567890123\r\nOK\r\n"
        });
        var reader = new AtDiagnosticReader(new AtCommandChannel(ch));
        var info = await reader.ReadIdentityAsync();

        Assert.True(info.LinkOk);
        Assert.Equal("SAMSUNG", info.Manufacturer);
        Assert.Equal("SM-A515F", info.Model);
        Assert.Equal("A515FXXU5", info.Firmware);
        Assert.Equal("351234567890123", info.Imei);
    }

    [Theory]
    [InlineData("351234567890123", "351234567890123")]
    [InlineData("+CGSN: \"351234567890123\"", "351234567890123")]
    [InlineData("", null)]
    public void Imei_is_normalized_to_digits(string raw, string? expected)
    {
        Assert.Equal(expected, AtDiagnosticReader.NormalizeImei(raw));
    }
}
