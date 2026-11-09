using Microsoft.Data.Sqlite;

namespace MUTASSIM_SERVER.Core.Billing;

/// <summary>
/// Durable accounts + credit wallet backed by SQLite. Credits are money-like, so every mutation runs inside a
/// transaction and all access is serialized through one connection: a balance can never go negative or be spent
/// twice, and nothing is lost on restart. The ledger is append-only and the single source of truth for history.
/// </summary>
public sealed class BillingStore : IAsyncDisposable
{
    private readonly SqliteConnection _db;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(12);

    public static readonly IReadOnlyList<OperationPrice> DefaultPricing =
    [
        new("read.info", "Read device info", "قراءة معلومات الجهاز", 0),
        new("assist.solution", "Smart Assist solution", "حل من المساعد الذكي", 1),
        new("samsung.flash", "Samsung firmware flash", "تفليش سامسنج", 5),
        new("network.unlock", "Official network unlock (IMEI)", "فك شبكة رسمي", 20),
    ];

    private BillingStore(SqliteConnection db) => _db = db;

    public static async Task<BillingStore> OpenAsync(string connectionString)
    {
        var db = new SqliteConnection(connectionString);
        await db.OpenAsync();
        var store = new BillingStore(db);
        await store.InitializeAsync();
        return store;
    }

    public static Task<BillingStore> OpenFileAsync(string path) =>
        OpenAsync(new SqliteConnectionStringBuilder { DataSource = path }.ToString());

    private async Task InitializeAsync()
    {
        await Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        await Exec("""
            CREATE TABLE IF NOT EXISTS accounts(
                id TEXT PRIMARY KEY, username TEXT NOT NULL UNIQUE COLLATE NOCASE, password TEXT NOT NULL,
                role INTEGER NOT NULL, parent_id TEXT NULL, balance INTEGER NOT NULL DEFAULT 0,
                enabled INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ledger(
                id INTEGER PRIMARY KEY AUTOINCREMENT, account_id TEXT NOT NULL, delta INTEGER NOT NULL,
                balance_after INTEGER NOT NULL, kind INTEGER NOT NULL, reference TEXT NOT NULL,
                actor_id TEXT NULL, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_ledger_account ON ledger(account_id, id DESC);
            CREATE TABLE IF NOT EXISTS sessions(
                token TEXT PRIMARY KEY, account_id TEXT NOT NULL, expires_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS charges(
                id TEXT PRIMARY KEY, account_id TEXT NOT NULL, operation TEXT NOT NULL, cost INTEGER NOT NULL,
                refunded INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS pricing(
                code TEXT PRIMARY KEY, title_en TEXT NOT NULL, title_ar TEXT NOT NULL, cost INTEGER NOT NULL);
            """);

        foreach (var p in DefaultPricing)
        {
            await Exec("INSERT OR IGNORE INTO pricing(code,title_en,title_ar,cost) VALUES($c,$e,$a,$cost)",
                ("$c", p.Code), ("$e", p.TitleEn), ("$a", p.TitleAr), ("$cost", p.Cost));
        }
    }

    // ================================================================= accounts

    /// <summary>Creates the first admin if no admin exists. Returns false if an admin already exists.</summary>
    public async Task<bool> EnsureAdminAsync(string username, string password)
    {
        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();
            var any = Convert.ToInt64(await Scalar(tx, "SELECT COUNT(*) FROM accounts WHERE role=0"));
            if (any > 0) { await tx.RollbackAsync(); return false; }

            await InsertAccount(tx, Guid.NewGuid(), username, PasswordHasher.Hash(password), AccountRole.Admin, null);
            await tx.CommitAsync();
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<BillingResult<Account>> CreateAccountAsync(Guid actorId, string username, string password, AccountRole role)
    {
        if (!PasswordHasher.IsStrongEnough(password)) return BillingResult<Account>.Fail(BillingError.InvalidCredentials);
        if (string.IsNullOrWhiteSpace(username)) return BillingResult<Account>.Fail(BillingError.InvalidCredentials);

        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();
            var actor = await LoadAccount(tx, actorId);
            if (actor is null) return BillingResult<Account>.Fail(BillingError.AccountNotFound);

            // Admin creates distributors or technicians; distributor creates only technicians under itself.
            var allowed = actor.Role switch
            {
                AccountRole.Admin => role is AccountRole.Distributor or AccountRole.Technician,
                AccountRole.Distributor => role is AccountRole.Technician,
                _ => false
            };
            if (!allowed) return BillingResult<Account>.Fail(BillingError.NotAuthorized);

            if (await LoadByUsername(tx, username) is not null)
                return BillingResult<Account>.Fail(BillingError.DuplicateUsername);

            var id = Guid.NewGuid();
            await InsertAccount(tx, id, username.Trim(), PasswordHasher.Hash(password), role, actor.Id);
            var created = await LoadAccount(tx, id);
            await tx.CommitAsync();
            return BillingResult<Account>.Success(created!);
        }
        finally { _lock.Release(); }
    }

    public async Task<BillingResult<bool>> SetEnabledAsync(Guid actorId, Guid targetId, bool enabled)
    {
        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();
            var actor = await LoadAccount(tx, actorId);
            var target = await LoadAccount(tx, targetId);
            if (actor is null || target is null) return BillingResult<bool>.Fail(BillingError.AccountNotFound);
            if (!await CanManage(tx, actor, target)) return BillingResult<bool>.Fail(BillingError.NotAuthorized);

            await Exec(tx, "UPDATE accounts SET enabled=$e WHERE id=$id", ("$e", enabled ? 1 : 0), ("$id", target.Id.ToString()));
            await tx.CommitAsync();
            return BillingResult<bool>.Success(enabled);
        }
        finally { _lock.Release(); }
    }

    public async Task<Account?> GetAccountAsync(Guid id)
    {
        await _lock.WaitAsync();
        try { return await LoadAccount(null, id); }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<Account>> ListChildrenAsync(Guid actorId)
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<Account>();
            await using var cmd = Command(null, "SELECT * FROM accounts WHERE parent_id=$p ORDER BY created_at", ("$p", actorId.ToString()));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(ReadAccount(r));
            return list;
        }
        finally { _lock.Release(); }
    }

    // ================================================================= auth

    public async Task<BillingResult<Session>> LoginAsync(string username, string password)
    {
        await _lock.WaitAsync();
        try
        {
            var account = await LoadByUsername(null, username);
            // Verify even when the account is missing, to keep the response time uniform.
            var ok = PasswordHasher.Verify(password, account?.Password ?? PasswordHasher.Hash("x"));
            if (account is null || !ok) return BillingResult<Session>.Fail(BillingError.InvalidCredentials);
            if (!account.Enabled) return BillingResult<Session>.Fail(BillingError.AccountDisabled);

            var token = PasswordHasher.NewToken();
            var expires = DateTime.UtcNow.Add(_sessionLifetime);
            await Exec("INSERT INTO sessions(token,account_id,expires_at) VALUES($t,$a,$e)",
                ("$t", token), ("$a", account.Id.ToString()), ("$e", Iso(expires)));
            await Exec("DELETE FROM sessions WHERE expires_at < $now", ("$now", Iso(DateTime.UtcNow)));
            return BillingResult<Session>.Success(new Session(token, account.Id, expires));
        }
        finally { _lock.Release(); }
    }

    public async Task<Account?> ValidateSessionAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await _lock.WaitAsync();
        try
        {
            var accountId = await Scalar(null, "SELECT account_id FROM sessions WHERE token=$t AND expires_at > $now",
                ("$t", token), ("$now", Iso(DateTime.UtcNow)));
            if (accountId is null) return null;
            var account = await LoadAccount(null, Guid.Parse((string)accountId));
            return account is { Enabled: true } ? account : null;
        }
        finally { _lock.Release(); }
    }

    public async Task LogoutAsync(string token)
    {
        await _lock.WaitAsync();
        try { await Exec("DELETE FROM sessions WHERE token=$t", ("$t", token)); }
        finally { _lock.Release(); }
    }

    // ================================================================= credits

    /// <summary>Admin only: adds credits to an account from nothing (stock top-up).</summary>
    public async Task<BillingResult<long>> MintAsync(Guid actorId, Guid targetId, long amount, string reference)
    {
        if (amount <= 0) return BillingResult<long>.Fail(BillingError.InvalidAmount);
        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();
            var actor = await LoadAccount(tx, actorId);
            var target = await LoadAccount(tx, targetId);
            if (actor is null || target is null) return BillingResult<long>.Fail(BillingError.AccountNotFound);
            if (actor.Role != AccountRole.Admin) return BillingResult<long>.Fail(BillingError.NotAuthorized);

            var balance = await Credit(tx, target.Id, amount, LedgerKind.Mint, reference, actor.Id);
            await tx.CommitAsync();
            return BillingResult<long>.Success(balance);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Moves credits from the actor's own balance to a direct child. Atomic: both sides succeed or neither.</summary>
    public async Task<BillingResult<long>> TransferAsync(Guid actorId, Guid targetId, long amount, string reference)
    {
        if (amount <= 0) return BillingResult<long>.Fail(BillingError.InvalidAmount);
        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();
            var actor = await LoadAccount(tx, actorId);
            var target = await LoadAccount(tx, targetId);
            if (actor is null || target is null) return BillingResult<long>.Fail(BillingError.AccountNotFound);
            if (actor.Role == AccountRole.Technician) return BillingResult<long>.Fail(BillingError.NotAuthorized);
            if (target.ParentId != actor.Id && actor.Role != AccountRole.Admin)
                return BillingResult<long>.Fail(BillingError.NotADescendant);
            if (actor.Balance < amount) return BillingResult<long>.Fail(BillingError.InsufficientBalance);

            await Credit(tx, actor.Id, -amount, LedgerKind.TransferOut, reference, actor.Id);
            var targetBalance = await Credit(tx, target.Id, amount, LedgerKind.TransferIn, reference, actor.Id);
            await tx.CommitAsync();
            return BillingResult<long>.Success(targetBalance);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Consumes credits for an operation. Fails (no deduction) when the balance is too low.</summary>
    public async Task<BillingResult<ChargeReceipt>> ChargeAsync(Guid actorId, string operationCode, string reference)
    {
        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();
            var actor = await LoadAccount(tx, actorId);
            if (actor is null) return BillingResult<ChargeReceipt>.Fail(BillingError.AccountNotFound);

            var cost = await Scalar(tx, "SELECT cost FROM pricing WHERE code=$c", ("$c", operationCode));
            if (cost is null) return BillingResult<ChargeReceipt>.Fail(BillingError.UnknownOperation);
            var price = Convert.ToInt64(cost);

            if (actor.Balance < price) return BillingResult<ChargeReceipt>.Fail(BillingError.InsufficientBalance);

            var balance = price == 0 ? actor.Balance : await Credit(tx, actor.Id, -price, LedgerKind.Charge, operationCode, actor.Id);
            var chargeId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await Exec(tx, "INSERT INTO charges(id,account_id,operation,cost,refunded,created_at) VALUES($i,$a,$o,$c,0,$t)",
                ("$i", chargeId.ToString()), ("$a", actor.Id.ToString()), ("$o", operationCode), ("$c", price), ("$t", Iso(now)));
            await tx.CommitAsync();
            return BillingResult<ChargeReceipt>.Success(new ChargeReceipt(chargeId, operationCode, price, balance, now));
        }
        finally { _lock.Release(); }
    }

    /// <summary>Returns the credits of a prior charge (e.g. the operation failed). Each charge refunds at most once.</summary>
    public async Task<BillingResult<long>> RefundAsync(Guid actorId, Guid chargeId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();
            await using var cmd = Command(tx, "SELECT account_id,cost,refunded FROM charges WHERE id=$i", ("$i", chargeId.ToString()));
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return BillingResult<long>.Fail(BillingError.ChargeNotFound);
            var owner = Guid.Parse(r.GetString(0));
            var cost = r.GetInt64(1);
            var refunded = r.GetInt64(2) == 1;
            await r.DisposeAsync();

            if (owner != actorId) return BillingResult<long>.Fail(BillingError.NotAuthorized);
            if (refunded) return BillingResult<long>.Fail(BillingError.AlreadyRefunded);

            await Exec(tx, "UPDATE charges SET refunded=1 WHERE id=$i", ("$i", chargeId.ToString()));
            var balance = cost == 0
                ? (await LoadAccount(tx, actorId))!.Balance
                : await Credit(tx, actorId, cost, LedgerKind.Refund, chargeId.ToString(), actorId);
            await tx.CommitAsync();
            return BillingResult<long>.Success(balance);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(Guid accountId, int max = 100)
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<LedgerEntry>();
            await using var cmd = Command(null, "SELECT * FROM ledger WHERE account_id=$a ORDER BY id DESC LIMIT $m",
                ("$a", accountId.ToString()), ("$m", max));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new LedgerEntry(r.GetInt64(0), Guid.Parse(r.GetString(1)), r.GetInt64(2), r.GetInt64(3),
                    (LedgerKind)r.GetInt32(4), r.GetString(5), r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)), DateTime.Parse(r.GetString(7))));
            return list;
        }
        finally { _lock.Release(); }
    }

    // ================================================================= pricing

    public async Task<IReadOnlyList<OperationPrice>> GetPricingAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<OperationPrice>();
            await using var cmd = Command(null, "SELECT code,title_en,title_ar,cost FROM pricing ORDER BY cost");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new OperationPrice(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
            return list;
        }
        finally { _lock.Release(); }
    }

    public async Task<BillingResult<bool>> SetPriceAsync(Guid actorId, string code, long cost)
    {
        if (cost < 0) return BillingResult<bool>.Fail(BillingError.InvalidAmount);
        await _lock.WaitAsync();
        try
        {
            var actor = await LoadAccount(null, actorId);
            if (actor is null) return BillingResult<bool>.Fail(BillingError.AccountNotFound);
            if (actor.Role != AccountRole.Admin) return BillingResult<bool>.Fail(BillingError.NotAuthorized);
            var n = await Exec("UPDATE pricing SET cost=$c WHERE code=$code", ("$c", cost), ("$code", code));
            return n == 0 ? BillingResult<bool>.Fail(BillingError.UnknownOperation) : BillingResult<bool>.Success(true);
        }
        finally { _lock.Release(); }
    }

    // ================================================================= helpers

    private async Task<long> Credit(SqliteTransaction tx, Guid accountId, long delta, LedgerKind kind, string reference, Guid? actorId)
    {
        var current = Convert.ToInt64(await Scalar(tx, "SELECT balance FROM accounts WHERE id=$id", ("$id", accountId.ToString())));
        var next = current + delta;
        if (next < 0) throw new InvalidOperationException("Balance would go negative — this must be checked before calling Credit.");
        await Exec(tx, "UPDATE accounts SET balance=$b WHERE id=$id", ("$b", next), ("$id", accountId.ToString()));
        await Exec(tx, "INSERT INTO ledger(account_id,delta,balance_after,kind,reference,actor_id,created_at) VALUES($a,$d,$b,$k,$r,$act,$t)",
            ("$a", accountId.ToString()), ("$d", delta), ("$b", next), ("$k", (int)kind), ("$r", reference),
            ("$act", actorId?.ToString()), ("$t", Iso(DateTime.UtcNow)));
        return next;
    }

    /// <summary>Admin can manage anyone; a parent can manage its own descendants.</summary>
    private async Task<bool> CanManage(SqliteTransaction tx, Account actor, Account target)
    {
        if (actor.Role == AccountRole.Admin) return true;
        if (actor.Id == target.Id) return false;
        var cursor = target.ParentId;
        while (cursor is { } id)
        {
            if (id == actor.Id) return true;
            cursor = (await LoadAccount(tx, id))?.ParentId;
        }
        return false;
    }

    private async Task InsertAccount(SqliteTransaction tx, Guid id, string username, string passwordHash, AccountRole role, Guid? parentId) =>
        await Exec(tx, "INSERT INTO accounts(id,username,password,role,parent_id,balance,enabled,created_at) VALUES($i,$u,$p,$r,$parent,0,1,$t)",
            ("$i", id.ToString()), ("$u", username), ("$p", passwordHash), ("$r", (int)role),
            ("$parent", parentId?.ToString()), ("$t", Iso(DateTime.UtcNow)));

    private async Task<Account?> LoadAccount(SqliteTransaction? tx, Guid id)
    {
        await using var cmd = Command(tx, "SELECT * FROM accounts WHERE id=$id", ("$id", id.ToString()));
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadAccount(r) : null;
    }

    private async Task<AccountWithHash?> LoadByUsername(SqliteTransaction? tx, string username)
    {
        await using var cmd = Command(tx, "SELECT * FROM accounts WHERE username=$u COLLATE NOCASE", ("$u", username.Trim()));
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var a = ReadAccount(r);
        return new AccountWithHash(a.Id, a.Username, a.Role, a.ParentId, a.Balance, a.Enabled, a.CreatedAt, r.GetString(2));
    }

    private sealed record AccountWithHash(Guid Id, string Username, AccountRole Role, Guid? ParentId, long Balance, bool Enabled, DateTime CreatedAt, string Password)
    {
        public static implicit operator Account(AccountWithHash a) => new(a.Id, a.Username, a.Role, a.ParentId, a.Balance, a.Enabled, a.CreatedAt);
    }

    private static Account ReadAccount(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(r.GetOrdinal("id"))),
        r.GetString(r.GetOrdinal("username")),
        (AccountRole)r.GetInt32(r.GetOrdinal("role")),
        r.IsDBNull(r.GetOrdinal("parent_id")) ? null : Guid.Parse(r.GetString(r.GetOrdinal("parent_id"))),
        r.GetInt64(r.GetOrdinal("balance")),
        r.GetInt32(r.GetOrdinal("enabled")) == 1,
        DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))));

    private SqliteCommand Command(SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private async Task<int> Exec(string sql, params (string, object?)[] args)
    {
        await using var cmd = Command(null, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> Exec(SqliteTransaction tx, string sql, params (string, object?)[] args)
    {
        await using var cmd = Command(tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        await using var cmd = Command(tx, sql, args);
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    private static string Iso(DateTime dt) => dt.ToString("O");

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        _lock.Dispose();
    }
}
