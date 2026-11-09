using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Plugins;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed class PluginRowViewModel(PluginInfo info)
{
    public string Name => info.Manifest?.Name is { Length: > 0 } n ? n : Path.GetFileName(info.Directory);
    public string Version => info.Manifest?.Version ?? "—";
    public string Vendor => info.Manifest?.Vendor is { Length: > 0 } v ? v : "—";
    public PluginState State => info.State;
    public bool Signed => info.IsSignatureVerified;
    public string? Message => info.Message;
    public string Devices => Join(info.Manifest?.SupportedDevices);
    public string Services => Join(info.Manifest?.Services);
    public string Requirements => Join(info.Manifest?.Requirements);
    public string Directory => info.Directory;

    private static string Join(string[]? items) => items is { Length: > 0 } ? string.Join(", ", items) : "—";
}

public sealed partial class PluginsViewModel(IPluginService plugins, IDialogService dialogs) : ObservableObject
{
    public string PluginsDirectory => plugins.PluginsDirectory;
    public int HostApiVersion => PluginHostApi.Version;
    public IReadOnlyList<PluginRowViewModel> Plugins { get; } =
        plugins.Plugins.Select(p => new PluginRowViewModel(p)).ToArray();

    [RelayCommand]
    private void OpenFolder()
    {
        System.IO.Directory.CreateDirectory(plugins.PluginsDirectory);
        dialogs.OpenInExplorer(plugins.PluginsDirectory);
    }
}
