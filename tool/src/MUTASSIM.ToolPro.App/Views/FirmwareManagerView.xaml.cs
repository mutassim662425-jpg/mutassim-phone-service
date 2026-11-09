using System.Windows;
using System.Windows.Controls;
using MUTASSIM.ToolPro.App.ViewModels;

namespace MUTASSIM.ToolPro.App.Views;

public partial class FirmwareManagerView : UserControl
{
    public FirmwareManagerView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && DataContext is FirmwareManagerViewModel vm)
            vm.AddFiles(paths);
    }
}
