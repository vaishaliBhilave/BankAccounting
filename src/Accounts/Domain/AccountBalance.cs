using BankAccounting.BuildingBlocks;

namespace BankAccounting.Accounts.Domain;

/// <summary>
/// Hard no-negative-balance rule lives here (domain), is serialized by a row lock (FOR UPDATE),
/// and is backstopped by the ck_no_negative CHECK constraint in the database.
/// </summary>
public sealed class AccountBalance
{
    public Guid AccountId { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public decimal LedgerBalance { get; private set; }
    public decimal HoldTotal { get; private set; }

    /// <summary>Ledger balance minus active holds. (Also a generated column in the DB for reporting.)</summary>
    public decimal AvailableBalance => LedgerBalance - HoldTotal;

    private AccountBalance() { } // EF Core

    internal static AccountBalance Opening(Guid accountId, string currencyCode) =>
        new() { AccountId = accountId, CurrencyCode = currencyCode, LedgerBalance = 0m, HoldTotal = 0m };

    public Result Debit(Money amount)
    {
        if (amount.Currency != CurrencyCode) return AccountErrors.CurrencyMismatch;
        if (!amount.IsPositive) return AccountErrors.InvalidAmount;
        if (amount.Amount > AvailableBalance) return AccountErrors.InsufficientFunds;
        LedgerBalance -= amount.Amount;
        return Result.Success();
    }

    public Result Credit(Money amount)
    {
        if (amount.Currency != CurrencyCode) return AccountErrors.CurrencyMismatch;
        if (!amount.IsPositive) return AccountErrors.InvalidAmount;
        LedgerBalance += amount.Amount;
        return Result.Success();
    }
}
