using System.Windows;
using MUTASSIM.ToolPro.App.ViewModels;

namespace MUTASSIM.ToolPro.App.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += ok =>
        {
            try { DialogResult = ok; } catch { /* DialogResult is only valid while shown with ShowDialog */ }
            Close();
        };
    }

    private void Pwd_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.Password = Pwd.Password;
    }
}
