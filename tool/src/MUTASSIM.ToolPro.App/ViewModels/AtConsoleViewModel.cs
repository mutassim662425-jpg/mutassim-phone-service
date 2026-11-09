using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Serial;

namespace MUTASSIM.ToolPro.App.ViewModels;

/// <summary>
/// Samsung AT Console: pick a COM port, connect, read diagnostics, and send read-only AT commands.
///
/// The engine is read-only. State-changing / engineering / protection commands are refused by the
/// <see cref="AtCommandChannel"/> and never sent; this page only surfaces that refusal to the user.
/// All port I/O runs off the UI thread so the window never freezes.
/// </summary>
public sealed partial class AtConsoleViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly ISerialPortEnumerator _ports;
    private readonly ISerialChannelFactory _factory;
    private readonly AtCommandLog _log = new();

    private ISerialChannel? _channel;
    private AtCommandChannel? _at;

    public AtConsoleViewModel(ISerialPortEnumerator ports, ISerialChannelFactory factory)
    {
        _ports = ports;
        _factory = factory;
    }

    public ObservableCollection<SerialPortInfo> Ports { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty] private SerialPortInfo? _selectedPort;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _commandText = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _info;

    [ObservableProperty] private string? _manufacturer;
    [ObservableProperty] private string? _model;
    [ObservableProperty] private string? _firmware;
    [ObservableProperty] private string? _imei;

    public Task OnNavigatedToAsync()
    {
        RefreshPorts();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        Error = null;
        var list = _ports.List();
        Ports.Clear();
        foreach (var p in list) Ports.Add(p);
        SelectedPort ??= SamsungComPortLocator.PickBest(list) ?? list.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        Error = null; Info = null;
        if (SelectedPort is null) { Error = L("AtConsole.NoPort"); return; }
        if (IsConnected) return;

        Busy = true;
        try
        {
            _channel = _factory.Create(SelectedPort.PortName);
            _at = new AtCommandChannel(_channel, _log);
            await Task.Run(() => _channel.Open());   // opening a COM port can block — keep it off the UI thread
            IsConnected = true;
            Info = Format("AtConsole.Connected", SelectedPort.PortName);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            CloseChannel();
        }
        finally
        {
            Busy = false;
            RebuildLog();
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        CloseChannel();
        Info = L("AtConsole.Disconnected");
        Error = null;
    }

    [RelayCommand]
    private async Task ReadIdentityAsync()
    {
        if (_at is null) { Error = L("AtConsole.NotConnected"); return; }
        Error = null; Info = null; Busy = true;
        try
        {
            var info = await new AtDiagnosticReader(_at).ReadIdentityAsync();
            Manufacturer = info.Manufacturer;
            Model = info.Model;
            Firmware = info.Firmware;
            Imei = info.Imei;
            if (!info.HasAnything) Error = L("AtConsole.NoResponse");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
            RebuildLog();
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (_at is null) { Error = L("AtConsole.NotConnected"); return; }
        if (string.IsNullOrWhiteSpace(CommandText)) return;
        Error = null; Info = null; Busy = true;
        try
        {
            await _at.SendConsoleAsync(CommandText);
            CommandText = "";
        }
        catch (StateChangeRefusedException ex)
        {
            Error = ex.Message; // the refusal is already recorded in the log
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
            RebuildLog();
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        _log.Clear();
        LogLines.Clear();
    }

    private void RebuildLog()
    {
        LogLines.Clear();
        foreach (var e in _log.Entries) LogLines.Add($"{e.Direction} {e.Text}");
    }

    private void CloseChannel()
    {
        try { _channel?.Close(); } catch { /* already gone */ }
        _channel?.Dispose();
        _channel = null;
        _at = null;
        IsConnected = false;
    }

    private static string L(string key) => LocalizationService.Get(key);
    private static string Format(string key, params object?[] args) => LocalizationService.Format(key, args);

    public void Dispose() => CloseChannel();
}
