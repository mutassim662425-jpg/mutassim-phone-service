using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using MUTASSIM.ToolPro.Core;

namespace MUTASSIM.ToolPro.App.Services;

/// <summary>Keeps view models free of WPF dialog types (testable MVVM).</summary>
public interface IDialogService
{
    bool Confirm(string message, bool dangerous = false);
    void Info(string message);
    void Error(string message);
    string[] OpenFiles(string filter, string? initialDirectory, bool multiselect = true);
    string? SaveFile(string filter, string defaultFileName);
    string? PickFolder(string? initialDirectory);
    void OpenInExplorer(string path);
    void CopyToClipboard(string text);
    /// <summary>Single-line text input. Returns null when cancelled.</summary>
    string? Prompt(string message, string defaultValue);
    /// <summary>Opens an https URL in the default browser (only https is allowed).</summary>
    void OpenUrl(string url);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string message, bool dangerous = false) =>
        Show(message, MessageBoxButton.YesNo, dangerous ? MessageBoxImage.Warning : MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    public void Info(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public string[] OpenFiles(string filter, string? initialDirectory, bool multiselect = true)
    {
        var dlg = new OpenFileDialog { Filter = filter, Multiselect = multiselect, CheckFileExists = true };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog(Owner) == true ? dlg.FileNames : [];
    }

    public string? SaveFile(string filter, string defaultFileName)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultFileName, AddExtension = true, OverwritePrompt = true };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string? initialDirectory)
    {
        var dlg = new OpenFolderDialog { Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    public void OpenInExplorer(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard temporarily locked by another process; ignore.
        }
    }

    public string? Prompt(string message, string defaultValue)
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue, MinWidth = 380, Margin = new Thickness(0, 10, 0, 12), FlowDirection = FlowDirection.LeftToRight
        };
        var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        panel.Children.Add(input);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = AppInfo.ProductName,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Owner,
            ShowInTaskbar = false
        };
        window.SetResourceReference(Window.BackgroundProperty, "Brush.Surface");
        window.SetResourceReference(Window.ForegroundProperty, "Brush.Text");
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var rtl = LocalizationService.Get("Lang.IsRtl") == "true";
        var options = rtl ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading : MessageBoxOptions.None;
        return Owner is { } owner
            ? MessageBox.Show(owner, message, AppInfo.ProductName, buttons, image, MessageBoxResult.None, options)
            : MessageBox.Show(message, AppInfo.ProductName, buttons, image, MessageBoxResult.None, options);
    }
}
