using System.Collections.Concurrent;

namespace MUTASSIM_SERVER.Core.Devices;

/// <summary>
/// One Android device as seen by an agent. DeviceId is the Android serial.
/// AgentId identifies which agent (PC) reported it (optional for old agents).
/// </summary>
public sealed record DeviceReport(
    string DeviceId,
    string? Brand,
    string? Manufacturer,
    string? Model,
    string? Android,
    string? Sdk,
    string? Build,
    string? Connection,
    string? AgentId = null
);

public sealed record DeviceReportEntry(DeviceReport Report, DateTime ReceivedAt);

public sealed record AgentStatus(string AgentId, DateTime LastSeen);

/// <summary>In-memory store of the latest report per device and last poll per agent.</summary>
public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceReportEntry> _devices = new();
    private readonly ConcurrentDictionary<string, DateTime> _agents = new();

    public DeviceReportEntry Upsert(DeviceReport report)
    {
        var entry = new DeviceReportEntry(report, DateTime.UtcNow);
        _devices[report.DeviceId] = entry;
        if (report.AgentId is not null)
            _agents[report.AgentId] = entry.ReceivedAt;
        return entry;
    }

    public void TouchAgent(string agentId) => _agents[agentId] = DateTime.UtcNow;

    public IReadOnlyList<DeviceReportEntry> Devices() =>
        _devices.Values.OrderBy(d => d.Report.DeviceId).ToList();

    public IReadOnlyList<AgentStatus> Agents() =>
        _agents.Select(a => new AgentStatus(a.Key, a.Value)).OrderBy(a => a.AgentId).ToList();
}
