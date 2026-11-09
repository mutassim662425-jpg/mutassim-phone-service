using System.Windows;
using System.Windows.Threading;

namespace MUTASSIM.ToolPro.App.Services;

/// <summary>Marshals engine events (raised on background threads) onto the UI thread.</summary>
public static class Ui
{
    public static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }
}
