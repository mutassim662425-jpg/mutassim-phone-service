using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.ViewModels;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.Fastboot;
using MUTASSIM.ToolPro.Infrastructure.Catalog;

namespace MUTASSIM.ToolPro.App.Workspace;

public sealed partial class GroupItem(ActionGroup group) : ObservableObject
{
    public ActionGroup Group { get; } = group;
    public string Title => Group.Title.ToUpperInvariant();
    [ObservableProperty] private bool _isSelected;
}

public sealed record PlannedItem(string Module, string Name, ImplementationStatus Status, string Phase, string? Notes);

/// <summary>Row of the model list (left panel).</summary>
public sealed class ModelRow(CatalogModel model, string group, ImageSource? thumbnail)
{
    public CatalogModel Model { get; } = model;
    public string Group { get; } = group;
    public string Name => Model.Name;
    public string Subtitle => Model.Subtitle;
    public ImageSource? Thumbnail { get; } = thumbnail;
    public bool HasThumbnail => Thumbnail is not null;
}

/// <summary>
/// A brand / chipset / protocol page: model search + list (left), device information card, mode tabs,
/// operation buttons and service status (centre). Operations always target the device selected in the
/// connection bar — choosing a catalog model only changes the reference information shown.
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly OperationRunner _runner;
    private readonly OperationLog _log;
    private readonly IDialogService _dialogs;
    private readonly DeviceCatalog _catalog;
    private readonly ImageLibrary _images;
    private List<ModelRow> _rows = [];
    private bool _disposed;

    public WorkspaceViewModel(
        WorkspaceDefinition definition,
        OperationRunner runner,
        OperationLog log,
        IDialogService dialogs,
        IModuleRegistry registry,
        DeviceContext deviceContext,
        DeviceCatalog catalog,
        ImageLibrary images,
        InfoPanelViewModel info)
    {
        Definition = definition;
        _runner = runner;
        _log = log;
        _dialogs = dialogs;
        DeviceContext = deviceContext;
        Info = info;
        Accent = BrandStyle.Accent(definition.Route);

        Planned = definition.ModuleIds
            .Select(registry.Find)
            .Where(m => m is not null)
            .SelectMany(m => m!.Services
                .Where(s => !s.IsAvailableNow)
                .Select(s => new PlannedItem(m.DisplayName, s.Name, s.Status,
                    s.PlannedPhase > 0 ? LocalizationService.Format("Module.PhaseN", s.PlannedPhase) : "—", s.Notes)))
            .ToArray();

        Groups = definition.Groups.Select(g => new GroupItem(g)).ToArray();
        SelectGroup(Groups.FirstOrDefault());

        _catalog = catalog;
        _images = images;
        BuildModelList();
        _catalog.Changed += OnCatalogChanged;

        DeviceContext.PropertyChanged += OnDeviceContextChanged;
        DeviceContext.Devices.CollectionChanged += OnDevicesChanged;
        Info.ShowDevice(DeviceContext.Selected);
    }

    public WorkspaceDefinition Definition { get; }
    public DeviceContext DeviceContext { get; }
    public InfoPanelViewModel Info { get; }
    public Brush Accent { get; }
    public string Title => Definition.Tab;
    public IReadOnlyList<GroupItem> Groups { get; }
    public IReadOnlyList<PlannedItem> Planned { get; }
    public OperationRunner Runner => _runner;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalog), nameof(CatalogHeader), nameof(IsCatalogSmall))]
    private int _modelCount;

    [ObservableProperty] private ICollectionView _modelsView = new ListCollectionView(new List<ModelRow>());

    public bool HasCatalog => ModelCount > 0;

    /// <summary>Brand / chipset pages with only the small built-in list offer the full Google list import.</summary>
    public bool IsCatalogSmall => IsModelPage && ModelCount < 200;
    public bool IsModelPage => Definition.Route is not ("ws.adb" or "ws.fastboot" or "ws.frp" or "ws.mdm");
    public bool HasPlanned => Planned.Count > 0;

    public string CatalogHeader => LocalizationService.Format("Ws.ModelsHeader", Title, ModelCount);

    public bool HasTerminal => Definition.Terminal != TerminalKind.None;
    public string TerminalPrefix => Definition.Terminal == TerminalKind.Fastboot ? "fastboot" : "adb";

    [ObservableProperty] private GroupItem? _selectedGroup;
    [ObservableProperty] private string _terminalText = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ModelRow? _selectedModel;
    [ObservableProperty] private int _visibleModels;

    public IReadOnlyList<ActionDefinition> Actions => SelectedGroup?.Group.Actions ?? [];

    partial void OnSearchTextChanged(string value)
    {
        ModelsView.Refresh();
        VisibleModels = ModelsView is ListCollectionView lcv ? lcv.Count : ModelsView.Cast<object>().Count();
    }

    partial void OnSelectedModelChanged(ModelRow? value)
    {
        if (value is not null) Info.ShowModel(value.Model);
        else Info.ShowDevice(DeviceContext.Selected);
    }

    [RelayCommand]
    private void SelectGroup(GroupItem? group)
    {
        if (group is null) return;
        foreach (var g in Groups) g.IsSelected = ReferenceEquals(g, group);
        SelectedGroup = group;
        OnPropertyChanged(nameof(Actions));
    }

    /// <summary>Click on a connected device: make it the target and show its live information.</summary>
    [RelayCommand]
    private void ShowDevice(DeviceInfo? device)
    {
        if (device is null) return;
        if (!_runner.IsBusy) DeviceContext.Selected = device;
        SelectedModel = null;
        Info.ShowDevice(DeviceContext.Selected);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private Task UpdateCatalogAsync() => _runner.RunAsync(ActionLibrary.UpdateCatalog);

    /// <summary>Brand pages list the brand's models; chipset pages list every model of that chipset family.</summary>
    private void BuildModelList()
    {
        var (models, byBrand) = Definition.Route switch
        {
            "ws.qualcomm" => (_catalog.ForFamily(ChipsetFamily.Qualcomm), true),
            "ws.mediatek" => (_catalog.ForFamily(ChipsetFamily.MediaTek), true),
            "ws.adb" or "ws.fastboot" or "ws.frp" or "ws.mdm" => ((IReadOnlyList<CatalogModel>)[], false),
            _ => (_catalog.ForBrand(Definition.Route), false)
        };
        _rows = models
            .OrderBy(m => byBrand ? BrandLabel(m.Brand) : m.Series, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => new ModelRow(m, byBrand ? BrandLabel(m.Brand) : m.Series, _images.Device(m, decodeHeight: 64)))
            .ToList();

        var view = new ListCollectionView(_rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModelRow.Group)));
        view.Filter = o => o is ModelRow r && DeviceCatalog.Matches(r.Model, SearchText);
        ModelsView = view;
        ModelCount = _rows.Count;
        VisibleModels = view.Count;
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        SelectedModel = null;
        BuildModelList();
    });

    [RelayCommand]
    private Task RunAsync(ActionDefinition? action) => action is null ? Task.CompletedTask : _runner.RunAsync(action);

    [RelayCommand]
    private async Task RunTerminalAsync()
    {
        var text = TerminalText.Trim();
        if (text.Length == 0) return;

        var decision = Definition.Terminal == TerminalKind.Fastboot
            ? FastbootCommandPolicy.Evaluate(text)
            : AdbCommandPolicy.Evaluate(text);

        switch (decision.Verdict)
        {
            case CommandVerdict.Blocked:
                _log.Error($"{TerminalPrefix} {text}");
                _log.Error(decision.Reason ?? "Blocked");
                return;
            case CommandVerdict.NeedsConfirmation:
                if (!_dialogs.Confirm($"{TerminalPrefix} {string.Join(' ', decision.Arguments)}\n\n{decision.Reason}\n\n{LocalizationService.Get("Ws.ConfirmRun")}", dangerous: true))
                {
                    _log.Warn(LocalizationService.Get("Run.CancelledByUser"));
                    return;
                }
                break;
        }

        var action = Definition.Terminal == TerminalKind.Fastboot
            ? ActionLibrary.FastbootTerminal(decision.Arguments)
            : ActionLibrary.AdbTerminal(decision.Arguments);
        await _runner.RunAsync(action);
    }

    private void OnDeviceContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceContext.Selected) && SelectedModel is null)
            Info.ShowDevice(DeviceContext.Selected);
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedModel is null) Info.ShowDevice(DeviceContext.Selected);
    }

    private static string BrandLabel(string route) =>
        WorkspaceCatalog.Find(route)?.Tab ?? route.Replace("ws.", "", StringComparison.Ordinal);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalog.Changed -= OnCatalogChanged;
        DeviceContext.PropertyChanged -= OnDeviceContextChanged;
        DeviceContext.Devices.CollectionChanged -= OnDevicesChanged;
    }
}
