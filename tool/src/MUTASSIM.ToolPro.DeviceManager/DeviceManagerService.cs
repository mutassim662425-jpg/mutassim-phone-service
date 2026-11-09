using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;

namespace MUTASSIM.ToolPro.DeviceManager;

/// <summary>
/// Central detection engine. Polls every registered <see cref="IDeviceDetector"/>, merges the results
/// by device key and raises Connected / Updated / Disconnected events.
///
/// Safety rules implemented here:
///  * Devices are keyed strictly by <see cref="DeviceInfo.Key"/>; data from two devices is never merged.
///  * If a detector fails, the devices it previously reported are kept (no false "disconnected").
///  * Detectors run with a per-scan timeout so one hung tool cannot freeze detection.
/// </summary>
public sealed class DeviceManagerService : IDeviceManager, IAsyncDisposable
{
    private static readonly TimeSpan DetectorTimeout = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IDeviceDetector> _detectors;
    private readonly IDeviceHistoryRepository _history;
    private readonly ISettingsService _settings;
    private readonly ILogger<DeviceManagerService> _logger;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly object _stateGate = new();

    private Dictionary<string, DeviceInfo> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DeviceInfo>> _lastBySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DetectorStatus> _detectorStatus = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _missedScans = new(StringComparer.Ordinal);
    public const int DisconnectAfterMissedScans = 2;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DeviceManagerService(
        IEnumerable<IDeviceDetector> detectors,
        IDeviceHistoryRepository history,
        ISettingsService settings,
        ILogger<DeviceManagerService> logger)
    {
        _detectors = detectors.ToArray();
        _history = history;
        _settings = settings;
        _logger = logger;

        foreach (var d in _detectors)
            _detectorStatus[d.Name] = new DetectorStatus(d.Name, d.IsAvailable, d.UnavailableReason, null, null, 0);
    }

    public IReadOnlyList<DeviceInfo> Devices
    {
        get { lock (_stateGate) return _devices.Values.OrderBy(d => d.FirstSeen).ToArray(); }
    }

    public IReadOnlyList<DetectorStatus> Detectors
    {
        get { lock (_stateGate) return _detectorStatus.Values.ToArray(); }
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;
    public event EventHandler? ScanCompleted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("Device manager started with {Count} detector(s): {Names}",
            _detectors.Count, string.Join(", ", _detectors.Select(d => d.Name)));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_loop is not null) await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        _logger.LogInformation("Device manager stopped");
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => ScanOnceAsync(cancellationToken);

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            do
            {
                await ScanOnceAsync(ct).ConfigureAwait(false);
                var seconds = Math.Clamp(_settings.Current.DeviceScanIntervalSeconds, 1, 30);
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
            } while (!ct.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Device detection loop crashed");
        }
    }

    /// <summary>Visible for tests: performs one full detection cycle.</summary>
    public async Task ScanOnceAsync(CancellationToken ct)
    {
        await _scanLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var scans = _detectors.Select(d => ScanDetectorAsync(d, ct)).ToArray();
            await Task.WhenAll(scans).ConfigureAwait(false);
            ApplyResults();
        }
        finally
        {
            _scanLock.Release();
        }

        ScanCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async Task ScanDetectorAsync(IDeviceDetector detector, CancellationToken ct)
    {
        if (!detector.IsAvailable)
        {
            lock (_stateGate)
            {
                _detectorStatus[detector.Name] = new DetectorStatus(detector.Name, false,
                    detector.UnavailableReason, DateTimeOffset.Now, null, 0);
                _lastBySource[detector.Name] = [];
            }
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DetectorTimeout);
            var found = await detector.ScanAsync(timeout.Token).ConfigureAwait(false);

            lock (_stateGate)
            {
                _lastBySource[detector.Name] = found.ToList();
                _detectorStatus[detector.Name] = new DetectorStatus(detector.Name, true, null,
                    DateTimeOffset.Now, null, found.Count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Detector {Detector} failed; keeping its previous results", detector.Name);
            lock (_stateGate)
            {
                var prev = _detectorStatus.GetValueOrDefault(detector.Name);
                _detectorStatus[detector.Name] = new DetectorStatus(detector.Name, true, null,
                    DateTimeOffset.Now, ex.Message, prev?.DeviceCount ?? 0);
            }
        }
    }

    private void ApplyResults()
    {
        var events = new List<DeviceChangedEventArgs>();

        lock (_stateGate)
        {
            var next = new Dictionary<string, DeviceInfo>(StringComparer.Ordinal);
            foreach (var device in MergeSamePhysicalDevice(_lastBySource.Values.SelectMany(x => x)))
            {
                if (next.ContainsKey(device.Key))
                {
                    _logger.LogWarning("Two detectors reported key {Key}; keeping the first", device.Key);
                    continue;
                }

                if (_devices.TryGetValue(device.Key, out var previous))
                {
                    var merged = device with { FirstSeen = previous.FirstSeen, LastSeen = DateTimeOffset.Now };
                    next[device.Key] = merged;
                    if (!previous.HasSameStateAs(merged))
                        events.Add(new DeviceChangedEventArgs(DeviceChangeKind.Updated, merged));
                }
                else
                {
                    var now = DateTimeOffset.Now;
                    var added = device with { FirstSeen = now, LastSeen = now };
                    next[device.Key] = added;
                    events.Add(new DeviceChangedEventArgs(DeviceChangeKind.Connected, added));
                }
            }

            foreach (var (key, old) in _devices)
            {
                if (next.ContainsKey(key))
                {
                    _missedScans.Remove(key);
                    continue;
                }

                // USB enumeration can briefly drop a device (mode switch, Windows re-enumeration).
                // Report a disconnect only after it is missing in two consecutive scans.
                var missed = _missedScans.GetValueOrDefault(key) + 1;
                if (missed < DisconnectAfterMissedScans)
                {
                    _missedScans[key] = missed;
                    next[key] = old;
                }
                else
                {
                    _missedScans.Remove(key);
                    events.Add(new DeviceChangedEventArgs(DeviceChangeKind.Disconnected, old));
                }
            }

            _devices = next;
        }

        foreach (var e in events)
        {
            _logger.LogInformation("Device {Kind}: {Name} [{Mode}]", e.Kind, e.Device.DisplayName, e.Device.Mode);
            if (e.Kind != DeviceChangeKind.Disconnected) _ = RecordHistoryAsync(e.Device);
            try
            {
                DeviceChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeviceChanged subscriber threw");
            }
        }
    }

    /// <summary>
    /// A raw USB entry and an ADB/Fastboot entry describe the SAME phone only when their serial numbers are
    /// identical. In that case the USB data (VID/PID, driver state, COM port) enriches the protocol entry and
    /// the USB entry is dropped. Entries without a matching serial are never merged.
    /// </summary>
    public static List<DeviceInfo> MergeSamePhysicalDevice(IEnumerable<DeviceInfo> devices)
    {
        var all = devices.ToList();
        var usb = all.Where(d => d.Source == "USB").ToList();
        var result = all.Where(d => d.Source != "USB").ToList();

        foreach (var u in usb)
        {
            var index = string.IsNullOrWhiteSpace(u.SerialNumber)
                ? -1
                : result.FindIndex(p => string.Equals(p.SerialNumber, u.SerialNumber, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                result.Add(u);
                continue;
            }

            var p = result[index];
            result[index] = p with
            {
                UsbVendorId = p.UsbVendorId ?? u.UsbVendorId,
                UsbProductId = p.UsbProductId ?? u.UsbProductId,
                UsbInstanceId = p.UsbInstanceId ?? u.UsbInstanceId,
                ComPort = p.ComPort ?? u.ComPort,
                DriverState = u.DriverState,
                Manufacturer = p.Manufacturer ?? u.Manufacturer
            };
        }
        return result;
    }

    private async Task RecordHistoryAsync(DeviceInfo device)
    {
        try
        {
            await _history.UpsertAsync(device).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record device history");
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
