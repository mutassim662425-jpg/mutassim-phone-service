using System.Windows;
using System.Windows.Controls;
using MUTASSIM.ToolPro.App.ViewModels;

namespace MUTASSIM.ToolPro.App.Views;

public partial class AccountView : UserControl
{
    public AccountView() => InitializeComponent();

    // PasswordBox does not support binding; push the value to the view-model on change.
    private void Pwd_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm && sender is PasswordBox box)
            vm.Password = box.Password;
    }
}
