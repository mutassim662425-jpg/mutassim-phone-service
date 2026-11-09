using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MUTASSIM.ToolPro.Api.Account;
using MUTASSIM.ToolPro.Core.Configuration;

namespace MUTASSIM.ToolPro.App.Services;

/// <summary>
/// Holds the signed-in account and credit balance for the whole app, and charges / refunds operations.
/// The session token lives in memory only. The billing server is the same MUTASSIM GSM CLOUD server
/// (AssistServerUrl in settings).
/// </summary>
public sealed partial class AccountSession(AccountClient client, ISettingsService settings) : ObservableObject
{
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn), nameof(StatusText), nameof(IsAdmin), nameof(IsDistributor))]
    private AccountDto? _account;

    public bool IsLoggedIn => Account is not null;
    public bool IsAdmin => Account?.Role == "Admin";
    public bool IsDistributor => Account?.Role is "Admin" or "Distributor";

    public string StatusText => Account is null
        ? LocalizationService.Get("Account.SignedOut")
        : $"{Account.Username} · {Account.Balance} {LocalizationService.Get("Account.Credits")}";

    public AccountClient Client => client;

    public Uri Server => AccountClient.ParseServerUrl(settings.Current.AssistServerUrl);

    public async Task<AccountDto> LoginAsync(string username, string password)
    {
        var result = await client.LoginAsync(Server, username, password, _cts.Token);
        Account = result.Account;
        return result.Account;
    }

    /// <summary>Self-registers a new account and signs in with it, so the tool can open straight away.</summary>
    public async Task<AccountDto> RegisterAsync(string username, string password)
    {
        var account = await client.RegisterAsync(Server, username, password, _cts.Token);
        await LoginAsync(username, password);
        return account;
    }

    public async Task LogoutAsync()
    {
        try { await client.LogoutAsync(Server, _cts.Token); }
        finally { Account = null; }
    }

    /// <summary>Refreshes the balance from the server (after a charge, transfer, etc.).</summary>
    public async Task RefreshAsync()
    {
        if (!client.IsLoggedIn) return;
        try { Account = await client.MeAsync(Server, _cts.Token); }
        catch (AccountException) { Account = null; }
    }

    /// <summary>
    /// Charges for an operation. Returns the charge id to refund on failure, or throws AccountException
    /// (e.g. insufficient balance) which the caller shows and aborts on.
    /// </summary>
    public async Task<Guid?> ChargeAsync(string operationCode, string reference)
    {
        var charge = await client.ChargeAsync(Server, operationCode, reference, _cts.Token);
        Account = Account is null ? null : Account with { Balance = charge.BalanceAfter };
        OnPropertyChanged(nameof(StatusText));
        return charge.Cost > 0 ? charge.ChargeId : null; // nothing to refund for a free operation
    }

    public async Task RefundAsync(Guid chargeId)
    {
        if (await client.RefundAsync(Server, chargeId, _cts.Token))
            await RefreshAsync();
    }
}
