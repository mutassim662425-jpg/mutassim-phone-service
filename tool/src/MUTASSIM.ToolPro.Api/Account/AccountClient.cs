using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MUTASSIM.ToolPro.Api.Account;

// JSON contract with MUTASSIM GSM CLOUD billing API (/api/auth, /api/credits, /api/operations).
public sealed record AccountDto(string Id, string Username, string Role, long Balance, bool Enabled, DateTime CreatedAt);
public sealed record LoginResult(string Token, DateTime ExpiresAt, AccountDto Account);
public sealed record OperationPriceDto(string Code, string TitleEn, string TitleAr, long Cost);
public sealed record ChargeDto(Guid ChargeId, string OperationCode, long Cost, long BalanceAfter, DateTime ChargedAt);
public sealed record LedgerRowDto(long Id, long Delta, long BalanceAfter, string Kind, string Reference, DateTime CreatedAt);

/// <summary>A readable error for the UI (never a stack trace). Carries the server's error code when there is one.</summary>
public sealed class AccountException(string message, string? code = null, Exception? inner = null) : Exception(message, inner)
{
    public string? Code { get; } = code;
    public bool InsufficientBalance => Code == "InsufficientBalance";
}

/// <summary>
/// HTTP client for the account / credit system. Holds the session token in memory only (never written to disk).
/// The token goes in the Authorization: Bearer header; the agent's MUTASSIM_API_KEY is NOT used here.
/// </summary>
public sealed class AccountClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string? _token;

    public bool IsLoggedIn => _token is not null;

    public void SetToken(string? token) => _token = token;

    public static Uri ParseServerUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new AccountException("Invalid server address. Example: http://localhost:5179");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public async Task<LoginResult> LoginAsync(Uri server, string username, string password, CancellationToken ct)
    {
        var body = new { username, password };
        var result = await Send<LoginResult>(server, HttpMethod.Post, "api/auth/login", body, auth: false, ct);
        _token = result.Token;
        return result;
    }

    public async Task LogoutAsync(Uri server, CancellationToken ct)
    {
        if (_token is null) return;
        try { await Send<JsonElement>(server, HttpMethod.Post, "api/auth/logout", null, auth: true, ct); }
        catch (AccountException) { /* logging out is best-effort */ }
        _token = null;
    }

    public Task<AccountDto> MeAsync(Uri server, CancellationToken ct) =>
        Unwrap<AccountDto>(server, HttpMethod.Get, "api/auth/me", null, "account", ct);

    public Task<List<OperationPriceDto>> PricingAsync(Uri server, CancellationToken ct) =>
        Send<List<OperationPriceDto>>(server, HttpMethod.Get, "api/operations/pricing", null, auth: false, ct);

    public Task<List<LedgerRowDto>> LedgerAsync(Uri server, CancellationToken ct) =>
        Send<List<LedgerRowDto>>(server, HttpMethod.Get, "api/credits/ledger", null, auth: true, ct);

    public Task<ChargeDto> ChargeAsync(Uri server, string operationCode, string reference, CancellationToken ct) =>
        Unwrap<ChargeDto>(server, HttpMethod.Post, "api/operations/charge", new { operationCode, reference }, "charge", ct);

    /// <summary>Returns credits for a failed operation. Never throws: a lost refund must not hide the real error.</summary>
    public async Task<bool> RefundAsync(Uri server, Guid chargeId, CancellationToken ct)
    {
        try
        {
            await Send<JsonElement>(server, HttpMethod.Post, "api/operations/refund", new { chargeId }, auth: true, ct);
            return true;
        }
        catch (AccountException) { return false; }
    }

    // ---- account management (admin / distributor) ----

    public Task<AccountDto> CreateAccountAsync(Uri server, string username, string password, string role, CancellationToken ct) =>
        Unwrap<AccountDto>(server, HttpMethod.Post, "api/accounts", new { username, password, role }, "account", ct);

    /// <summary>Anonymous self-registration (start-up window). Creates a zero-balance technician account.</summary>
    public async Task<AccountDto> RegisterAsync(Uri server, string username, string password, CancellationToken ct)
    {
        var e = await Send<JsonElement>(server, HttpMethod.Post, "api/auth/register", new { username, password }, auth: false, ct);
        return e.GetProperty("account").Deserialize<AccountDto>(Json)!;
    }

    public Task<List<AccountDto>> ChildrenAsync(Uri server, CancellationToken ct) =>
        Send<List<AccountDto>>(server, HttpMethod.Get, "api/accounts/children", null, auth: true, ct);

    public async Task<long> MintAsync(Uri server, Guid accountId, long amount, CancellationToken ct)
    {
        var e = await Send<JsonElement>(server, HttpMethod.Post, "api/credits/mint", new { accountId, amount }, auth: true, ct);
        return e.GetProperty("balance").GetInt64();
    }

    public async Task<long> TransferAsync(Uri server, Guid accountId, long amount, CancellationToken ct)
    {
        var e = await Send<JsonElement>(server, HttpMethod.Post, "api/credits/transfer", new { accountId, amount }, auth: true, ct);
        return e.GetProperty("targetBalance").GetInt64();
    }

    // ---- transport ----

    private async Task<T> Unwrap<T>(Uri server, HttpMethod method, string path, object? body, string property, CancellationToken ct)
    {
        var e = await Send<JsonElement>(server, method, path, body, auth: true, ct);
        return e.GetProperty(property).Deserialize<T>(Json)!;
    }

    private async Task<T> Send<T>(Uri server, HttpMethod method, string path, object? body, bool auth, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(server, path));
        if (auth)
        {
            if (_token is null) throw new AccountException("Please sign in first.", "NotAuthorized");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
        }
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AccountException($"Cannot reach the server at {server}. Is it running?", "Network", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _token = null;
                throw new AccountException("Your session expired. Please sign in again.", "Unauthorized");
            }
            if (!response.IsSuccessStatusCode)
            {
                var (message, code) = ParseError(text, response.StatusCode);
                throw new AccountException(message, code);
            }
            if (string.IsNullOrWhiteSpace(text)) throw new AccountException("The server returned an empty answer.");
            try { return JsonSerializer.Deserialize<T>(text, Json)!; }
            catch (JsonException ex) { throw new AccountException("The server answer is not valid JSON (wrong address?).", "Json", ex); }
        }
    }

    private static (string Message, string? Code) ParseError(string body, HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : $"Server error {(int)status}";
            var code = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return (message, code);
        }
        catch (JsonException)
        {
            return ($"Server error {(int)status}", null);
        }
    }
}
