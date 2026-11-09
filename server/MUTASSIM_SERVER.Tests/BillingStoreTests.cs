using MUTASSIM_SERVER.Core.Billing;
using Xunit;

namespace MUTASSIM_SERVER.Tests;

public class BillingStoreTests : IAsyncLifetime
{
    private string _file = "";
    private BillingStore _store = null!;
    private Guid _admin;

    public async Task InitializeAsync()
    {
        _file = Path.Combine(Path.GetTempPath(), "mutassim-billing-" + Guid.NewGuid().ToString("N") + ".db");
        _store = await BillingStore.OpenFileAsync(_file);
        Assert.True(await _store.EnsureAdminAsync("admin", "admin-pass-1"));
        _admin = (await LoginId("admin", "admin-pass-1"));
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_file)!, Path.GetFileName(_file) + "*"))
            try { File.Delete(f); } catch { /* wal/shm */ }
    }

    private async Task<Guid> LoginId(string u, string p)
    {
        var s = await _store.LoginAsync(u, p);
        Assert.True(s.Ok, u);
        return s.Value!.AccountId;
    }

    // ---- accounts & auth ------------------------------------------------

    [Fact]
    public async Task Only_one_admin_can_be_seeded()
    {
        Assert.False(await _store.EnsureAdminAsync("admin2", "x-another-pass"));
        Assert.Equal(BillingError.InvalidCredentials, (await _store.LoginAsync("admin2", "x-another-pass")).Error);
    }

    [Fact]
    public async Task Password_is_never_stored_in_plain_text()
    {
        // SQLite keeps the database file open (and on Windows, locked), and recent writes may still be in the
        // -wal side file, so read every database file with shared access and check all of them.
        var text = new System.Text.StringBuilder();
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(_file)!, Path.GetFileName(_file) + "*"))
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[fs.Length];
            await fs.ReadExactlyAsync(bytes);
            text.Append(System.Text.Encoding.UTF8.GetString(bytes));
        }
        Assert.DoesNotContain("admin-pass-1", text.ToString());
    }

    [Fact]
    public async Task Wrong_password_and_disabled_account_cannot_log_in()
    {
        var dist = await _store.CreateAccountAsync(_admin, "dist", "dist-pass-1", AccountRole.Distributor);
        Assert.True(dist.Ok);
        Assert.Equal(BillingError.InvalidCredentials, (await _store.LoginAsync("dist", "wrong")).Error);

        await _store.SetEnabledAsync(_admin, dist.Value!.Id, false);
        Assert.Equal(BillingError.AccountDisabled, (await _store.LoginAsync("dist", "dist-pass-1")).Error);
    }

    [Fact]
    public async Task Session_token_validates_and_logs_out()
    {
        var s = await _store.LoginAsync("admin", "admin-pass-1");
        Assert.NotNull(await _store.ValidateSessionAsync(s.Value!.Token));
        await _store.LogoutAsync(s.Value.Token);
        Assert.Null(await _store.ValidateSessionAsync(s.Value.Token));
        Assert.Null(await _store.ValidateSessionAsync("not-a-token"));
    }

    [Fact]
    public async Task Duplicate_username_is_refused()
    {
        Assert.True((await _store.CreateAccountAsync(_admin, "tech", "tech-pass-1", AccountRole.Technician)).Ok);
        Assert.Equal(BillingError.DuplicateUsername, (await _store.CreateAccountAsync(_admin, "TECH", "tech-pass-2", AccountRole.Technician)).Error);
    }

    [Fact]
    public async Task Role_creation_rules_are_enforced()
    {
        var dist = (await _store.CreateAccountAsync(_admin, "d1", "d1-pass-1", AccountRole.Distributor)).Value!;
        var tech = (await _store.CreateAccountAsync(dist.Id, "t1", "t1-pass-1", AccountRole.Technician)).Value!;

        // Distributor cannot create a distributor or an admin.
        Assert.Equal(BillingError.NotAuthorized, (await _store.CreateAccountAsync(dist.Id, "d2", "d2-pass-1", AccountRole.Distributor)).Error);
        // Technician cannot create anyone.
        Assert.Equal(BillingError.NotAuthorized, (await _store.CreateAccountAsync(tech.Id, "t2", "t2-pass-1", AccountRole.Technician)).Error);
    }

    // ---- credits --------------------------------------------------------

    [Fact]
    public async Task Mint_transfer_and_charge_move_credits_correctly()
    {
        var dist = (await _store.CreateAccountAsync(_admin, "d", "d-pass-1", AccountRole.Distributor)).Value!;
        var tech = (await _store.CreateAccountAsync(dist.Id, "t", "t-pass-1", AccountRole.Technician)).Value!;

        Assert.Equal(100, (await _store.MintAsync(_admin, dist.Id, 100, "stock")).Value);
        Assert.Equal(30, (await _store.TransferAsync(dist.Id, tech.Id, 30, "topup")).Value);
        Assert.Equal(70, (await _store.GetAccountAsync(dist.Id))!.Balance);

        var charge = await _store.ChargeAsync(tech.Id, "samsung.flash", "A515F");
        Assert.True(charge.Ok);
        Assert.Equal(5, charge.Value!.Cost);
        Assert.Equal(25, charge.Value.BalanceAfter);
    }

    [Fact]
    public async Task Only_admin_can_mint()
    {
        var dist = (await _store.CreateAccountAsync(_admin, "d", "d-pass-1", AccountRole.Distributor)).Value!;
        Assert.Equal(BillingError.NotAuthorized, (await _store.MintAsync(dist.Id, dist.Id, 50, "self")).Error);
    }

    [Fact]
    public async Task Transfer_only_to_own_child_and_only_what_you_have()
    {
        var d1 = (await _store.CreateAccountAsync(_admin, "d1", "d1-pass-1", AccountRole.Distributor)).Value!;
        var t1 = (await _store.CreateAccountAsync(d1.Id, "t1", "t1-pass-1", AccountRole.Technician)).Value!;
        var d2 = (await _store.CreateAccountAsync(_admin, "d2", "d2-pass-1", AccountRole.Distributor)).Value!;
        var t2 = (await _store.CreateAccountAsync(d2.Id, "t2", "t2-pass-1", AccountRole.Technician)).Value!;
        await _store.MintAsync(_admin, d1.Id, 10, "x");

        Assert.Equal(BillingError.NotADescendant, (await _store.TransferAsync(d1.Id, t2.Id, 5, "x")).Error); // not my child
        Assert.Equal(BillingError.InsufficientBalance, (await _store.TransferAsync(d1.Id, t1.Id, 999, "x")).Error); // my child, too much
    }

    [Fact]
    public async Task Charge_refuses_when_balance_is_too_low_and_does_not_deduct()
    {
        var tech = (await _store.CreateAccountAsync(_admin, "t", "t-pass-1", AccountRole.Technician)).Value!;
        await _store.MintAsync(_admin, tech.Id, 3, "x");
        Assert.Equal(BillingError.InsufficientBalance, (await _store.ChargeAsync(tech.Id, "samsung.flash", "x")).Error); // costs 5
        Assert.Equal(3, (await _store.GetAccountAsync(tech.Id))!.Balance);
    }

    [Fact]
    public async Task Refund_returns_credits_once_only()
    {
        var tech = (await _store.CreateAccountAsync(_admin, "t", "t-pass-1", AccountRole.Technician)).Value!;
        await _store.MintAsync(_admin, tech.Id, 10, "x");
        var charge = (await _store.ChargeAsync(tech.Id, "samsung.flash", "x")).Value!;
        Assert.Equal(5, (await _store.GetAccountAsync(tech.Id))!.Balance);

        Assert.Equal(10, (await _store.RefundAsync(tech.Id, charge.ChargeId)).Value);
        Assert.Equal(BillingError.AlreadyRefunded, (await _store.RefundAsync(tech.Id, charge.ChargeId)).Error);
        Assert.Equal(BillingError.ChargeNotFound, (await _store.RefundAsync(tech.Id, Guid.NewGuid())).Error);
    }

    [Fact]
    public async Task Free_operation_charges_zero_and_still_records_a_charge()
    {
        var tech = (await _store.CreateAccountAsync(_admin, "t", "t-pass-1", AccountRole.Technician)).Value!;
        var charge = await _store.ChargeAsync(tech.Id, "read.info", "x"); // cost 0
        Assert.True(charge.Ok);
        Assert.Equal(0, charge.Value!.Cost);
        Assert.Equal(0, charge.Value.BalanceAfter);
    }

    [Fact]
    public async Task Unknown_operation_is_refused()
    {
        var tech = (await _store.CreateAccountAsync(_admin, "t", "t-pass-1", AccountRole.Technician)).Value!;
        await _store.MintAsync(_admin, tech.Id, 100, "x");
        Assert.Equal(BillingError.UnknownOperation, (await _store.ChargeAsync(tech.Id, "frp.bypass", "x")).Error);
    }

    // ---- the money-critical property -----------------------------------

    [Fact]
    public async Task Concurrent_charges_never_overspend()
    {
        var tech = (await _store.CreateAccountAsync(_admin, "t", "t-pass-1", AccountRole.Technician)).Value!;
        await _store.MintAsync(_admin, tech.Id, 20, "x"); // exactly 4 flashes of cost 5

        var attempts = Enumerable.Range(0, 50).Select(_ => _store.ChargeAsync(tech.Id, "samsung.flash", "race")).ToArray();
        var results = await Task.WhenAll(attempts);

        var succeeded = results.Count(r => r.Ok);
        Assert.Equal(4, succeeded);
        Assert.Equal(0, (await _store.GetAccountAsync(tech.Id))!.Balance);

        // Ledger must reconcile exactly to zero: +20 mint, -5 four times.
        var ledger = await _store.GetLedgerAsync(tech.Id, 1000);
        Assert.Equal(0, ledger.Sum(e => e.Delta));
        Assert.Equal(4, ledger.Count(e => e.Kind == LedgerKind.Charge));
    }

    [Fact]
    public async Task Concurrent_transfers_cannot_drain_below_zero()
    {
        var dist = (await _store.CreateAccountAsync(_admin, "d", "d-pass-1", AccountRole.Distributor)).Value!;
        var tech = (await _store.CreateAccountAsync(dist.Id, "t", "t-pass-1", AccountRole.Technician)).Value!;
        await _store.MintAsync(_admin, dist.Id, 10, "x");

        var attempts = Enumerable.Range(0, 30).Select(_ => _store.TransferAsync(dist.Id, tech.Id, 1, "race")).ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(10, results.Count(r => r.Ok));
        Assert.Equal(0, (await _store.GetAccountAsync(dist.Id))!.Balance);
        Assert.Equal(10, (await _store.GetAccountAsync(tech.Id))!.Balance);
    }

    [Fact]
    public async Task Balance_survives_a_restart()
    {
        var tech = (await _store.CreateAccountAsync(_admin, "t", "t-pass-1", AccountRole.Technician)).Value!;
        await _store.MintAsync(_admin, tech.Id, 42, "x");
        await _store.DisposeAsync();

        _store = await BillingStore.OpenFileAsync(_file);
        Assert.Equal(42, (await _store.GetAccountAsync(tech.Id))!.Balance);
    }
}
