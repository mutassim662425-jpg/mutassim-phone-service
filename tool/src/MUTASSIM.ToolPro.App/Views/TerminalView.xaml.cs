using System.Windows;
using System.Windows.Controls;
using MUTASSIM.ToolPro.App.ViewModels;

namespace MUTASSIM.ToolPro.App.Views;

public partial class TerminalView : UserControl
{
    private TerminalViewModel? _vm;

    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = e.NewValue as TerminalViewModel;
        if (_vm is not null)
        {
            _vm.LinesAppended += OnLinesAppended;
            ScrollToEnd();
        }
    }

    private void OnLinesAppended(object? sender, EventArgs e)
    {
        if (_vm?.AutoScroll == true) ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void Detach()
    {
        if (_vm is not null) _vm.LinesAppended -= OnLinesAppended;
        _vm = null;
    }
}
