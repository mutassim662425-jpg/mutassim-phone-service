using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.Infrastructure;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed record LanguageOption(string Code, string Name);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly IExternalToolLocator _tools;
    private readonly IDialogService _dialogs;
    private readonly MainViewModel _shell;
    private readonly AppPaths _paths;
    private readonly MUTASSIM.ToolPro.App.Workspace.OperationRunner _runner;

    public SettingsViewModel(ISettingsService settings, LocalizationService localization, IExternalToolLocator tools,
        IDialogService dialogs, MainViewModel shell, AppPaths paths, MUTASSIM.ToolPro.App.Workspace.OperationRunner runner)
    {
        _runner = runner;
        _settings = settings;
        _localization = localization;
        _tools = tools;
        _dialogs = dialogs;
        _shell = shell;
        _paths = paths;

        Languages = LocalizationService.Supported.Select(l => new LanguageOption(l.Code, l.NativeName)).ToArray();
        var s = settings.Current;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == s.Language) ?? Languages[^1];
        _licenseServerUrl = s.LicenseServerUrl ?? "";
        _updateFeedUrl = s.UpdateFeedUrl ?? "";
        _statusServerUrl = s.StatusServerUrl ?? "";
        _platformToolsPath = s.PlatformToolsPath ?? "";
        _scanInterval = s.DeviceScanIntervalSeconds;
        _allowUnsignedPlugins = s.AllowUnsignedPlugins;
        RefreshToolPaths();
    }

    public IReadOnlyList<LanguageOption> Languages { get; }
    public string DataFolder => _paths.Root;

    [ObservableProperty] private LanguageOption _selectedLanguage;
    [ObservableProperty] private string _licenseServerUrl;
    [ObservableProperty] private string _updateFeedUrl;
    [ObservableProperty] private string _statusServerUrl;
    [ObservableProperty] private string _platformToolsPath;
    [ObservableProperty] private int _scanInterval;
    [ObservableProperty] private bool _allowUnsignedPlugins;
    [ObservableProperty] private string _adbPath = "";
    [ObservableProperty] private string _fastbootPath = "";

    [RelayCommand]
    private void BrowsePlatformTools()
    {
        var folder = _dialogs.PickFolder(string.IsNullOrWhiteSpace(PlatformToolsPath) ? null : PlatformToolsPath);
        if (folder is not null) PlatformToolsPath = folder;
    }

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenInExplorer(_paths.Root);

    [RelayCommand]
    private Task UpdateCatalogAsync() => _runner.RunAsync(MUTASSIM.ToolPro.App.Workspace.ActionLibrary.UpdateCatalog);

    /// <summary>Folder for device photos supplied by the owner (topaz.png, SM-A515F.png, redmi-note-12-4g.png …).</summary>
    [RelayCommand]
    private void OpenDevicesFolder()
    {
        var dir = Path.Combine(_paths.Root, "devices");
        Directory.CreateDirectory(dir);
        _dialogs.OpenInExplorer(dir);
    }

    /// <summary>Folder for additional catalog files (*.json, same format as the built-in list).</summary>
    [RelayCommand]
    private void OpenCatalogFolder()
    {
        var dir = Path.Combine(_paths.Root, "catalog");
        Directory.CreateDirectory(dir);
        _dialogs.OpenInExplorer(dir);
    }

    /// <summary>Folder for brand images supplied by the owner (samsung.png, xiaomi.png …). Restart / reopen Home to apply.</summary>
    [RelayCommand]
    private void OpenBrandsFolder()
    {
        var dir = Path.Combine(_paths.Root, "brands");
        Directory.CreateDirectory(dir);
        _dialogs.OpenInExplorer(dir);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        foreach (var (url, key) in new[]
                 {
                     (LicenseServerUrl, "Settings.LicenseServer"),
                     (UpdateFeedUrl, "Settings.UpdateFeed"),
                     (StatusServerUrl, "Settings.StatusServer")
                 })
        {
            if (!string.IsNullOrWhiteSpace(url) &&
                (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            {
                _dialogs.Error(LocalizationService.Format("Settings.InvalidUrl", LocalizationService.Get(key)));
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(PlatformToolsPath) && !Directory.Exists(PlatformToolsPath))
        {
            _dialogs.Error(LocalizationService.Get("Settings.FolderMissing"));
            return;
        }

        var updated = _settings.Current.Clone();
        updated.Language = SelectedLanguage.Code;
        updated.LicenseServerUrl = Normalize(LicenseServerUrl);
        updated.UpdateFeedUrl = Normalize(UpdateFeedUrl);
        updated.StatusServerUrl = Normalize(StatusServerUrl);
        updated.PlatformToolsPath = Normalize(PlatformToolsPath);
        updated.DeviceScanIntervalSeconds = Math.Clamp(ScanInterval, 1, 30);
        updated.AllowUnsignedPlugins = AllowUnsignedPlugins;

        var languageChanged = updated.Language != _localization.Language;
        await _settings.SaveAsync(updated);
        RefreshToolPaths();
        _shell.RefreshTools();

        if (languageChanged)
            _localization.Apply(updated.Language); // re-creates this page in the new language
        else
            _dialogs.Info(LocalizationService.Get("Settings.Saved"));
    }

    private void RefreshToolPaths()
    {
        var adb = _tools.Locate("adb");
        var fastboot = _tools.Locate("fastboot");
        AdbPath = adb.Path ?? LocalizationService.Get("Status.ToolMissing");
        FastbootPath = fastboot.Path ?? LocalizationService.Get("Status.ToolMissing");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
