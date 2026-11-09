using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.Api.Account;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;

namespace MUTASSIM.ToolPro.App.ViewModels;

/// <summary>
/// Account page: sign in / out, shows the credit balance and ledger, and (for admin / distributor)
/// lets the owner create sub-accounts and add or transfer credits to them.
/// </summary>
public sealed partial class AccountViewModel(AccountSession session) : ObservableObject, IPageViewModel
{
    public AccountSession Session { get; } = session;

    public ObservableCollection<LedgerRowDto> Ledger { get; } = [];
    public ObservableCollection<AccountDto> Children { get; } = [];
    public ObservableCollection<OperationPriceDto> Pricing { get; } = [];

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _info;
    [ObservableProperty] private bool _busy;

    // New sub-account
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private bool _newIsDistributor;

    // Credit top-up for a selected child
    [ObservableProperty] private AccountDto? _selectedChild;
    [ObservableProperty] private string _creditAmount = "";

    private static bool Arabic => LocalizationService.Get("Lang.IsRtl") == "true";

    public async Task OnNavigatedToAsync()
    {
        if (Session.IsLoggedIn) await RefreshAsync();
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        Error = null; Info = null; Busy = true;
        try
        {
            await Session.LoginAsync(Username.Trim(), Password);
            Password = "";
            await RefreshAsync();
        }
        catch (AccountException ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await Session.LogoutAsync();
        Ledger.Clear(); Children.Clear();
        Info = null; Error = null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Error = null; Busy = true;
        try
        {
            await Session.RefreshAsync();
            var server = Session.Server;

            Pricing.Clear();
            foreach (var p in await Session.Client.PricingAsync(server, default)) Pricing.Add(p);

            Ledger.Clear();
            foreach (var row in await Session.Client.LedgerAsync(server, default)) Ledger.Add(row);

            Children.Clear();
            if (Session.IsDistributor)
                foreach (var c in await Session.Client.ChildrenAsync(server, default)) Children.Add(c);
        }
        catch (AccountException ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task CreateChildAsync()
    {
        Error = null; Info = null;
        if (string.IsNullOrWhiteSpace(NewUsername) || NewPassword.Length < 6)
        {
            Error = LocalizationService.Get("Account.NeedUserPass");
            return;
        }
        Busy = true;
        try
        {
            // Admin may create distributors; a distributor creates technicians.
            var role = Session.IsAdmin && NewIsDistributor ? "Distributor" : "Technician";
            var created = await Session.Client.CreateAccountAsync(Session.Server, NewUsername.Trim(), NewPassword, role, default);
            Info = LocalizationService.Format("Account.Created", created.Username, created.Role);
            NewUsername = ""; NewPassword = "";
            await RefreshAsync();
        }
        catch (AccountException ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task AddCreditsAsync()
    {
        Error = null; Info = null;
        if (SelectedChild is null) { Error = LocalizationService.Get("Account.PickChild"); return; }
        if (!long.TryParse(CreditAmount, out var amount) || amount <= 0) { Error = LocalizationService.Get("Account.BadAmount"); return; }
        Busy = true;
        try
        {
            var id = Guid.Parse(SelectedChild.Id);
            // Admin mints new credits; a distributor transfers from its own balance.
            var balance = Session.IsAdmin
                ? await Session.Client.MintAsync(Session.Server, id, amount, default)
                : await Session.Client.TransferAsync(Session.Server, id, amount, default);
            Info = LocalizationService.Format("Account.CreditsAdded", amount, SelectedChild.Username, balance);
            CreditAmount = "";
            await RefreshAsync();
        }
        catch (AccountException ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    public string PriceLine(OperationPriceDto p) => $"{(Arabic ? p.TitleAr : p.TitleEn)} — {p.Cost}";
}
