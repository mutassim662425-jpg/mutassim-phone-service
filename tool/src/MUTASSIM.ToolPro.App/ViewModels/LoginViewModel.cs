using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.Api.Account;
using MUTASSIM.ToolPro.App.Services;

namespace MUTASSIM.ToolPro.App.ViewModels;

/// <summary>
/// The start-up sign-in / sign-up gate. On success it signs the <see cref="AccountSession"/> in and asks the
/// window to close with a positive result, after which the app opens the main window. Registration creates a
/// zero-balance technician on the server and then signs in automatically.
/// </summary>
public sealed partial class LoginViewModel(AccountSession session) : ObservableObject
{
    private readonly AccountSession _session = session;

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _isSignUp;

    /// <summary>Raised with true when authenticated (open the app) or false when the user cancels (exit).</summary>
    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private void ToggleMode()
    {
        IsSignUp = !IsSignUp;
        Error = null;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Email) || Password.Length < 6)
        {
            Error = LocalizationService.Get("Login.NeedEmailPass");
            return;
        }

        Busy = true;
        try
        {
            if (IsSignUp) await _session.RegisterAsync(Email.Trim(), Password);
            else await _session.LoginAsync(Email.Trim(), Password);
            CloseRequested?.Invoke(true);
        }
        catch (AccountException ex) { Error = ex.Message; }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
