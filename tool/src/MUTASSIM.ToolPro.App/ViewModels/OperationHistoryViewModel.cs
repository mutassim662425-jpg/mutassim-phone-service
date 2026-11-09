using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Database;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed record StatusFilterOption(OperationStatus? Status, string Title);

public sealed partial class OperationHistoryViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IOperationRepository _repository;
    private readonly IOperationTracker _tracker;
    private readonly IDialogService _dialogs;
    private readonly ILogger<OperationHistoryViewModel> _logger;

    public OperationHistoryViewModel(IOperationRepository repository, IOperationTracker tracker,
        IDialogService dialogs, ILogger<OperationHistoryViewModel> logger)
    {
        _repository = repository;
        _tracker = tracker;
        _dialogs = dialogs;
        _logger = logger;

        StatusOptions =
        [
            new(null, LocalizationService.Get("History.AllStatuses")),
            .. Enum.GetValues<OperationStatus>().Select(s => new StatusFilterOption(s, LocalizationService.ForEnum(s)))
        ];
        _selectedStatus = StatusOptions[0];
        _tracker.OperationFinished += OnOperationFinished;
    }

    public IReadOnlyList<StatusFilterOption> StatusOptions { get; }
    public ObservableCollection<OperationRecord> Records { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private StatusFilterOption _selectedStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private OperationRecord? _selectedRecord;

    [ObservableProperty] private int _resultCount;

    public Task OnNavigatedToAsync() => LoadAsync();

    partial void OnSelectedStatusChanged(StatusFilterOption value) => _ = LoadAsync();

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = _dialogs.SaveFile("CSV (*.csv)|*.csv", $"operations_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        if (path is null) return;
        try
        {
            await OperationCsvExporter.ExportAsync(Records, path);
            _dialogs.Info(LocalizationService.Format("History.Exported", Records.Count, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Export failed");
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedRecord is not { } record) return;
        if (!_dialogs.Confirm(LocalizationService.Format("History.ConfirmDelete", record.Id), dangerous: true)) return;
        await _repository.DeleteAsync(record.Id);
        _logger.LogInformation("Operation #{Id} deleted by user", record.Id);
        await LoadAsync();
    }

    private bool CanDeleteSelected() => SelectedRecord is not null;

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (!_dialogs.Confirm(LocalizationService.Get("History.ConfirmClear"), dangerous: true)) return;
        var count = await _repository.DeleteAllAsync();
        _logger.LogWarning("Operation history cleared by user ({Count} rows)", count);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var rows = await _repository.QueryAsync(new OperationQuery(
            Text: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            Status: SelectedStatus?.Status,
            Limit: 2000));

        Records.Clear();
        foreach (var r in rows) Records.Add(r);
        ResultCount = Records.Count;
    }

    private void OnOperationFinished(object? sender, OperationRecord e) => Ui.Post(() => _ = LoadAsync());

    public void Dispose() => _tracker.OperationFinished -= OnOperationFinished;
}
