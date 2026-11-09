namespace MUTASSIM_SERVER.Core.Billing;

/// <summary>Role hierarchy: Admin creates Distributors, Distributors create Technicians.</summary>
public enum AccountRole
{
    Admin,
    Distributor,
    Technician
}

public sealed record Account(
    Guid Id,
    string Username,
    AccountRole Role,
    Guid? ParentId,
    long Balance,
    bool Enabled,
    DateTime CreatedAt);

/// <summary>One movement of credits. The ledger is append-only; balance is the running total.</summary>
public enum LedgerKind
{
    Mint,         // admin added credits (no source)
    TransferOut,  // credits left this account toward a child
    TransferIn,   // credits arrived from a parent
    Charge,       // an operation consumed credits
    Refund,       // a failed operation returned credits
    Adjust        // manual admin correction
}

public sealed record LedgerEntry(
    long Id,
    Guid AccountId,
    long Delta,
    long BalanceAfter,
    LedgerKind Kind,
    string Reference,
    Guid? ActorId,
    DateTime CreatedAt);

public sealed record OperationPrice(string Code, string TitleEn, string TitleAr, long Cost);

public sealed record ChargeReceipt(Guid ChargeId, string OperationCode, long Cost, long BalanceAfter, DateTime ChargedAt);

public sealed record Session(string Token, Guid AccountId, DateTime ExpiresAt);

public enum BillingError
{
    None,
    AccountNotFound,
    DuplicateUsername,
    InvalidCredentials,
    AccountDisabled,
    InsufficientBalance,
    NotAuthorized,
    NotADescendant,
    InvalidAmount,
    UnknownOperation,
    ChargeNotFound,
    AlreadyRefunded
}

public sealed record BillingResult<T>(T? Value, BillingError Error)
{
    public bool Ok => Error == BillingError.None;
    public static BillingResult<T> Success(T value) => new(value, BillingError.None);
    public static BillingResult<T> Fail(BillingError error) => new(default, error);
}
