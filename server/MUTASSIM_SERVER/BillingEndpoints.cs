using MUTASSIM_SERVER.Core.Billing;

namespace MUTASSIM_SERVER;

// Request bodies (field names must stay in sync with the tool's AccountClient).
public sealed record LoginRequest(string Username, string Password);
public sealed record CreateAccountRequest(string Username, string Password, string Role);
public sealed record CreditRequest(Guid AccountId, long Amount, string? Reference);
public sealed record ChargeRequest(string OperationCode, string? Reference);
public sealed record RefundRequest(Guid ChargeId);
public sealed record SetPriceRequest(string Code, long Cost);
public sealed record SetEnabledRequest(bool Enabled);

public sealed record AccountDto(string Id, string Username, string Role, long Balance, bool Enabled, DateTime CreatedAt)
{
    public static AccountDto From(Account a) => new(a.Id.ToString(), a.Username, a.Role.ToString(), a.Balance, a.Enabled, a.CreatedAt);
}

public static class BillingEndpoints
{
    public static void MapBilling(this WebApplication app, BillingStore store)
    {
        // Resolve the caller's account from the Authorization: Bearer <token> header.
        async Task<Account?> Caller(HttpContext ctx)
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
            return await store.ValidateSessionAsync(token);
        }

        IResult Map(BillingError error) => error switch
        {
            BillingError.AccountNotFound or BillingError.ChargeNotFound => Results.NotFound(Err(error, "Not found")),
            BillingError.DuplicateUsername => Results.Conflict(Err(error, "Username already exists")),
            BillingError.InvalidCredentials => Results.BadRequest(Err(error, "Invalid username or password (password must be 6+ characters)")),
            BillingError.AccountDisabled => Results.Json(Err(error, "Account is disabled"), statusCode: 403),
            BillingError.InsufficientBalance => Results.Json(Err(error, "Not enough credits"), statusCode: 402),
            BillingError.NotAuthorized or BillingError.NotADescendant => Results.Json(Err(error, "Not allowed"), statusCode: 403),
            BillingError.InvalidAmount => Results.BadRequest(Err(error, "Amount must be positive")),
            BillingError.UnknownOperation => Results.BadRequest(Err(error, "Unknown operation code")),
            BillingError.AlreadyRefunded => Results.Conflict(Err(error, "This charge was already refunded")),
            _ => Results.BadRequest(Err(error, "Request failed"))
        };

        static object Err(BillingError e, string message) => new { success = false, error = e.ToString(), message };

        // ---- auth ----

        app.MapPost("/api/auth/login", async (LoginRequest? req) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(Err(BillingError.InvalidCredentials, "Username and password are required"));
            var result = await store.LoginAsync(req.Username, req.Password);
            if (!result.Ok) return Map(result.Error);
            var account = await store.GetAccountAsync(result.Value!.AccountId);
            return Results.Ok(new { success = true, token = result.Value.Token, expiresAt = result.Value.ExpiresAt, account = AccountDto.From(account!) });
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) await store.LogoutAsync(header[7..].Trim());
            return Results.Ok(new { success = true });
        });

        app.MapGet("/api/auth/me", async (HttpContext ctx) =>
        {
            var me = await Caller(ctx);
            return me is null ? Unauthorized() : Results.Ok(new { success = true, account = AccountDto.From(me) });
        });

        // ---- accounts ----

        app.MapPost("/api/accounts", async (HttpContext ctx, CreateAccountRequest? req) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            if (req is null || !Enum.TryParse<AccountRole>(req.Role, ignoreCase: true, out var role))
                return Results.BadRequest(Err(BillingError.NotAuthorized, "Role must be Distributor or Technician"));
            var result = await store.CreateAccountAsync(me.Id, req.Username, req.Password, role);
            return result.Ok ? Results.Ok(new { success = true, account = AccountDto.From(result.Value!) }) : Map(result.Error);
        });

        app.MapGet("/api/accounts/children", async (HttpContext ctx) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            var children = await store.ListChildrenAsync(me.Id);
            return Results.Ok(children.Select(AccountDto.From));
        });

        app.MapPost("/api/accounts/{id:guid}/enabled", async (HttpContext ctx, Guid id, SetEnabledRequest? req) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            if (req is null) return Results.BadRequest(Err(BillingError.InvalidAmount, "Enabled flag required"));
            var result = await store.SetEnabledAsync(me.Id, id, req.Enabled);
            return result.Ok ? Results.Ok(new { success = true, enabled = req.Enabled }) : Map(result.Error);
        });

        // ---- credits ----

        app.MapPost("/api/credits/mint", async (HttpContext ctx, CreditRequest? req) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            if (req is null) return Results.BadRequest(Err(BillingError.InvalidAmount, "Account and amount required"));
            var result = await store.MintAsync(me.Id, req.AccountId, req.Amount, req.Reference ?? "mint");
            return result.Ok ? Results.Ok(new { success = true, balance = result.Value }) : Map(result.Error);
        });

        app.MapPost("/api/credits/transfer", async (HttpContext ctx, CreditRequest? req) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            if (req is null) return Results.BadRequest(Err(BillingError.InvalidAmount, "Account and amount required"));
            var result = await store.TransferAsync(me.Id, req.AccountId, req.Amount, req.Reference ?? "transfer");
            return result.Ok ? Results.Ok(new { success = true, targetBalance = result.Value }) : Map(result.Error);
        });

        app.MapGet("/api/credits/ledger", async (HttpContext ctx, int? max) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            var ledger = await store.GetLedgerAsync(me.Id, Math.Clamp(max ?? 100, 1, 1000));
            return Results.Ok(ledger.Select(e => new { e.Id, delta = e.Delta, balanceAfter = e.BalanceAfter, kind = e.Kind.ToString(), e.Reference, e.CreatedAt }));
        });

        // ---- operations ----

        app.MapGet("/api/operations/pricing", async () => Results.Ok(await store.GetPricingAsync()));

        app.MapPost("/api/operations/charge", async (HttpContext ctx, ChargeRequest? req) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            if (req is null || string.IsNullOrWhiteSpace(req.OperationCode))
                return Results.BadRequest(Err(BillingError.UnknownOperation, "OperationCode required"));
            var result = await store.ChargeAsync(me.Id, req.OperationCode, req.Reference ?? req.OperationCode);
            return result.Ok ? Results.Ok(new { success = true, charge = result.Value }) : Map(result.Error);
        });

        app.MapPost("/api/operations/refund", async (HttpContext ctx, RefundRequest? req) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            if (req is null) return Results.BadRequest(Err(BillingError.ChargeNotFound, "ChargeId required"));
            var result = await store.RefundAsync(me.Id, req.ChargeId);
            return result.Ok ? Results.Ok(new { success = true, balance = result.Value }) : Map(result.Error);
        });

        app.MapPost("/api/pricing", async (HttpContext ctx, SetPriceRequest? req) =>
        {
            var me = await Caller(ctx);
            if (me is null) return Unauthorized();
            if (req is null || string.IsNullOrWhiteSpace(req.Code))
                return Results.BadRequest(Err(BillingError.UnknownOperation, "Code and cost required"));
            var result = await store.SetPriceAsync(me.Id, req.Code, req.Cost);
            return result.Ok ? Results.Ok(new { success = true }) : Map(result.Error);
        });

        static IResult Unauthorized() => Results.Json(new { success = false, message = "Please sign in" }, statusCode: 401);
    }
}
