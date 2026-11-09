using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Api.Assist;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed record AssistProblemItem(string Code, string Title);

public enum AssistStepState { Ready, Running, Succeeded, Failed, NotRun, Unavailable }

/// <summary>One recommended step. Run uses the tool's own action; the server only chose which one.</summary>
public sealed partial class AssistStepItem(AssistStepDto dto, ActionDefinition? action, bool arabic) : ObservableObject
{
    public AssistStepDto Dto { get; } = dto;
    public ActionDefinition? Action { get; } = action;
    public int Order => Dto.Order;
    public string Title => arabic ? Dto.TitleAr : Dto.TitleEn;
    public string Reason => arabic ? Dto.ReasonAr : Dto.ReasonEn;
    public bool IsDangerous => Action is not null && AssistActionMap.IsDangerous(Action);
    public bool CanRun => Action is not null && State is not AssistStepState.Running;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(StateBrush), nameof(CanRun))]
    private AssistStepState _state = action is null ? AssistStepState.Unavailable : AssistStepState.Ready;

    [ObservableProperty] private string _resultText = "";

    public string StateText => LocalizationService.Get($"Assist.State.{State}");

    public Brush StateBrush => State switch
    {
        AssistStepState.Succeeded => Res("Brush.Success"),
        AssistStepState.Failed or AssistStepState.Unavailable => Res("Brush.Error"),
        AssistStepState.Running => Res("Brush.Accent"),
        AssistStepState.NotRun => Res("Brush.Warning"),
        _ => Res("Brush.TextMuted")
    };

    private static Brush Res(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>
/// Smart Assist page: sends the selected problem and the connected phone's state to MUTASSIM GSM CLOUD,
/// shows the recommended steps, runs them with the tool's own actions, and reports each result back.
/// </summary>
public sealed partial class AssistViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly AssistClient _client;
    private readonly ISettingsService _settings;
    private readonly OperationRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly OperationLog _log;
    private readonly ILogger<AssistViewModel> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Guid _sessionId;
    private Uri? _sessionServer;

    public AssistViewModel(AssistClient client, ISettingsService settings, OperationRunner runner,
        DeviceContext deviceContext, IDialogService dialogs, OperationLog log, ILogger<AssistViewModel> logger)
    {
        _client = client;
        _settings = settings;
        _runner = runner;
        _dialogs = dialogs;
        _log = log;
        _logger = logger;
        DeviceContext = deviceContext;
        _serverUrl = settings.Current.AssistServerUrl;
        DeviceContext.PropertyChanged += OnDeviceChanged;
    }

    public DeviceContext DeviceContext { get; }
    public OperationRunner Runner => _runner;
    public ObservableCollection<AssistProblemItem> Problems { get; } = [];
    public ObservableCollection<AssistStepItem> Steps { get; } = [];
    public ObservableCollection<string> Guidance { get; } = [];

    private static bool Arabic => LocalizationService.Get("Lang.IsRtl") == "true";

    [ObservableProperty] private string _serverUrl;
    [ObservableProperty] private string _serverStatus = "";
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string? _securityWarning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskServerCommand))]
    private AssistProblemItem? _selectedProblem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskServerCommand), nameof(RunAllCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasAnswer;
    [ObservableProperty] private string? _errorText;

    public string DeviceText => DeviceContext.Selected is { } d
        ? $"{d.DisplayManufacturer} {d.DisplayName}  ·  {d.Mode.ToDisplayText()}"
        : LocalizationService.Get("Assist.NoDevice");

    public async Task OnNavigatedToAsync() => await CheckServerAsync();

    // ------------------------------------------------------------------ server

    [RelayCommand]
    private async Task SaveServerAsync()
    {
        try
        {
            var uri = AssistClient.ParseServerUrl(ServerUrl);
            var s = _settings.Current.Clone();
            s.AssistServerUrl = uri.ToString().TrimEnd('/');
            await _settings.SaveAsync(s);
            ServerUrl = s.AssistServerUrl;
        }
        catch (AssistServerException ex)
        {
            ErrorText = ex.Message;
            return;
        }
        await CheckServerAsync();
    }

    [RelayCommand]
    private async Task CheckServerAsync()
    {
        ErrorText = null;
        try
        {
            var uri = AssistClient.ParseServerUrl(ServerUrl);
            SecurityWarning = AssistClient.IsInsecure(uri) ? LocalizationService.Get("Assist.InsecureWarning") : null;

            var health = await _client.CheckAsync(uri, _cts.Token);
            IsOnline = health.Success;
            ServerStatus = health.Success
                ? $"{LocalizationService.Get("Assist.Online")}  ·  {health.Server} v{health.Version}"
                : LocalizationService.Get("Assist.Offline");

            var problems = await _client.GetProblemsAsync(uri, _cts.Token);
            var previous = SelectedProblem?.Code;
            Problems.Clear();
            foreach (var p in problems)
                Problems.Add(new AssistProblemItem(p.Code, Arabic ? p.TitleAr : p.TitleEn));
            SelectedProblem = Problems.FirstOrDefault(p => p.Code == previous) ?? Problems.FirstOrDefault();
        }
        catch (AssistServerException ex)
        {
            IsOnline = false;
            ServerStatus = LocalizationService.Get("Assist.Offline");
            ErrorText = ex.Message;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool CanAsk() => !IsBusy && SelectedProblem is not null;

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskServerAsync()
    {
        if (SelectedProblem is null) return;
        IsBusy = true;
        ErrorText = null;
        try
        {
            var uri = AssistClient.ParseServerUrl(ServerUrl);
            var device = DeviceContext.Selected;
            var request = new AssistRequestDto(await ClientIdAsync(), SelectedProblem.Code, ToDto(device), Arabic ? "ar" : "en");

            var answer = await _client.DiagnoseAsync(uri, request, _cts.Token);
            _sessionId = answer.Response.SessionId;
            _sessionServer = uri;

            Summary = Arabic ? answer.Response.SummaryAr : answer.Response.SummaryEn;
            Steps.Clear();
            foreach (var step in answer.Steps)
                Steps.Add(new AssistStepItem(step, AssistActionMap.Resolve(step.ActionId, device), Arabic));
            Guidance.Clear();
            foreach (var note in answer.Response.Guidance)
                Guidance.Add(Arabic ? note.Ar : note.En);
            foreach (var id in answer.RejectedActionIds)
                Guidance.Add(LocalizationService.Format("Assist.Rejected", id));

            HasAnswer = true;
            _log.Header(LocalizationService.Get("Nav.Assist"));
            _log.Info($"{SelectedProblem.Title} → {Steps.Count} step(s)");
            _logger.LogInformation("Assist session {Session}: {Problem}, {Count} steps, {Rejected} rejected",
                _sessionId, request.Problem, Steps.Count, answer.RejectedActionIds.Count);
        }
        catch (AssistServerException ex)
        {
            ErrorText = ex.Message;
            IsOnline = false;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------------ run steps

    [RelayCommand]
    private Task RunStepAsync(AssistStepItem? step) => step is null ? Task.CompletedTask : RunOneAsync(step);

    private bool CanRunAll() => !IsBusy;

    /// <summary>Runs the safe steps in order and stops at the first failure. Dangerous steps are skipped.</summary>
    [RelayCommand(CanExecute = nameof(CanRunAll))]
    private async Task RunAllAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var step in Steps.Where(s => s.State is AssistStepState.Ready or AssistStepState.Failed or AssistStepState.NotRun))
            {
                if (step.IsDangerous)
                {
                    _log.Warn(LocalizationService.Format("Assist.SkippedDangerous", step.Title));
                    continue;
                }
                if (!await RunOneAsync(step)) break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RunOneAsync(AssistStepItem step)
    {
        if (step.Action is null)
        {
            _log.Error(LocalizationService.Format("Assist.Rejected", step.Dto.ActionId));
            return false;
        }

        step.State = AssistStepState.Running;
        var result = await _runner.RunAsync(step.Action);

        if (result is null)
        {
            step.State = AssistStepState.NotRun;
            step.ResultText = LocalizationService.Get("Assist.NotRunHint");
            return false;
        }

        step.State = result.Success ? AssistStepState.Succeeded : AssistStepState.Failed;
        step.ResultText = result.Message;

        if (_sessionServer is not null)
        {
            var sent = await _client.SendFeedbackAsync(_sessionServer,
                new AssistFeedbackDto(_sessionId, step.Dto.ActionId, result.Success, result.Message), _cts.Token);
            if (!sent) _log.Muted(LocalizationService.Get("Assist.FeedbackLost"));
        }

        return result.Success;
    }

    // ------------------------------------------------------------------ helpers

    private static AssistDeviceDto? ToDto(DeviceInfo? d) => d is null
        ? null
        : new AssistDeviceDto(d.Mode.ToString(), d.Brand, d.Manufacturer, d.Model, d.Chipset, d.AndroidVersion, d.BootloaderState);

    private async Task<string> ClientIdAsync()
    {
        if (_settings.Current.AssistClientId is { Length: > 0 } id) return id;
        var s = _settings.Current.Clone();
        s.AssistClientId = "TOOL-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        await _settings.SaveAsync(s);
        return s.AssistClientId;
    }

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceContext.Selected)) OnPropertyChanged(nameof(DeviceText));
    }

    public void Dispose()
    {
        DeviceContext.PropertyChanged -= OnDeviceChanged;
        _cts.Cancel(); // not disposed: a running step may still read the token while the page closes
    }
}
