using Microsoft.Extensions.Logging.Abstractions;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.DeviceManager;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class DeviceManagerTests
{
    private sealed class FakeDetector(string name) : IDeviceDetector
    {
        public List<DeviceInfo> Current { get; } = [];
        public bool Throw { get; set; }
        public string Name => name;
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public Task<IReadOnlyList<DeviceInfo>> ScanAsync(CancellationToken cancellationToken) =>
            Throw ? throw new InvalidOperationException("detector failure")
                  : Task.FromResult<IReadOnlyList<DeviceInfo>>(Current.ToList());
    }

    private sealed class NullHistory : IDeviceHistoryRepository
    {
        public Task UpsertAsync(DeviceInfo device, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DeviceHistoryEntry>> RecentAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceHistoryEntry>>([]);
    }

    private sealed class FixedSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public event EventHandler? Changed { add { } remove { } }
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static DeviceInfo Phone(string serial, ConnectionMode mode = ConnectionMode.Adb) =>
        new() { Key = "adb:" + serial, Mode = mode, Source = "fake", Model = "Model-" + serial };

    private static (DeviceManagerService Manager, List<DeviceChangedEventArgs> Events) Create(params IDeviceDetector[] detectors)
    {
        var manager = new DeviceManagerService(detectors, new NullHistory(), new FixedSettings(),
            NullLogger<DeviceManagerService>.Instance);
        var events = new List<DeviceChangedEventArgs>();
        manager.DeviceChanged += (_, e) => events.Add(e);
        return (manager, events);
    }

    [Fact]
    public async Task Connect_update_disconnect_events()
    {
        var detector = new FakeDetector("adb");
        var (manager, events) = Create(detector);

        detector.Current.Add(Phone("A"));
        await manager.ScanOnceAsync(CancellationToken.None);
        Assert.Equal(DeviceChangeKind.Connected, Assert.Single(events).Kind);

        await manager.ScanOnceAsync(CancellationToken.None);
        Assert.Single(events); // no change → no event

        detector.Current[0] = Phone("A", ConnectionMode.Recovery);
        await manager.ScanOnceAsync(CancellationToken.None);
        Assert.Equal(DeviceChangeKind.Updated, events[^1].Kind);

        detector.Current.Clear();
        await manager.ScanOnceAsync(CancellationToken.None);
        Assert.NotEqual(DeviceChangeKind.Disconnected, events[^1].Kind); // one missed scan = grace period
        Assert.Single(manager.Devices);

        await manager.ScanOnceAsync(CancellationToken.None);
        Assert.Equal(DeviceChangeKind.Disconnected, events[^1].Kind);
        Assert.Empty(manager.Devices);
    }

    [Fact]
    public async Task Brief_usb_dropout_does_not_flap()
    {
        var detector = new FakeDetector("usb");
        var (manager, events) = Create(detector);
        detector.Current.Add(Phone("A"));
        await manager.ScanOnceAsync(CancellationToken.None);

        detector.Current.Clear();                       // dropped for one scan
        await manager.ScanOnceAsync(CancellationToken.None);
        detector.Current.Add(Phone("A"));               // back again
        await manager.ScanOnceAsync(CancellationToken.None);

        Assert.Single(events);                          // only the original Connected
        Assert.Single(manager.Devices);
    }

    [Fact]
    public async Task Two_devices_are_never_merged()
    {
        var detector = new FakeDetector("adb");
        var (manager, _) = Create(detector);
        detector.Current.Add(Phone("A"));
        detector.Current.Add(Phone("B"));

        await manager.ScanOnceAsync(CancellationToken.None);

        Assert.Equal(2, manager.Devices.Count);
        Assert.Contains(manager.Devices, d => d.Model == "Model-A");
        Assert.Contains(manager.Devices, d => d.Model == "Model-B");
    }

    [Fact]
    public async Task Failing_detector_does_not_fake_a_disconnect()
    {
        var detector = new FakeDetector("adb");
        var (manager, events) = Create(detector);
        detector.Current.Add(Phone("A"));
        await manager.ScanOnceAsync(CancellationToken.None);

        detector.Throw = true;
        await manager.ScanOnceAsync(CancellationToken.None);

        Assert.Single(manager.Devices);
        Assert.DoesNotContain(events, e => e.Kind == DeviceChangeKind.Disconnected);
        Assert.Equal("detector failure", Assert.Single(manager.Detectors).LastError);
    }

    [Fact]
    public async Task No_detectors_means_no_devices()
    {
        var (manager, events) = Create();
        await manager.ScanOnceAsync(CancellationToken.None);
        Assert.Empty(manager.Devices);
        Assert.Empty(events);
    }
}
