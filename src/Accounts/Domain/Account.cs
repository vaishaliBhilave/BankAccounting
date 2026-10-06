using BankAccounting.BuildingBlocks;

namespace BankAccounting.Accounts.Domain;

public enum AccountStatus : short { Active = 1, Frozen = 2, Closed = 3 }

/// <summary>
/// Customer-facing (sub-ledger) account. Its money lives in the ledger; the AccountBalance row is a
/// materialized, lock-protected projection updated in the same DB transaction as every posting.
/// </summary>
public sealed class Account
{
    public Guid Id { get; private set; }
    public string AccountNumber { get; private set; } = "";
    public Guid CustomerId { get; private set; }

    /// <summary>The GL control account (e.g. 2100 Savings Deposits) this account rolls up to.</summary>
    public long LedgerAccountId { get; private set; }

    public string CurrencyCode { get; private set; } = "";
    public AccountStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public AccountBalance Balance { get; private set; } = null!;

    private Account() { } // EF Core

    public static Account Open(Guid id, string accountNumber, Guid customerId, long ledgerAccountId,
        string currencyCode, DateTimeOffset now)
    {
        var ccy = Money.NormalizeCurrency(currencyCode);
        return new Account
        {
            Id = id,
            AccountNumber = accountNumber,
            CustomerId = customerId,
            LedgerAccountId = ledgerAccountId,
            CurrencyCode = ccy,
            Status = AccountStatus.Active,
            OpenedAt = now,
            Balance = AccountBalance.Opening(id, ccy)
        };
    }

    /// <summary>Frozen accounts may still receive money but cannot pay out.</summary>
    public Result CanDebit() => Status switch
    {
        AccountStatus.Active => Result.Success(),
        AccountStatus.Frozen => AccountErrors.Frozen,
        _ => AccountErrors.Closed
    };

    public Result CanCredit() => Status switch
    {
        AccountStatus.Active or AccountStatus.Frozen => Result.Success(),
        _ => AccountErrors.Closed
    };

    public Result Freeze()
    {
        if (Status != AccountStatus.Active) return AccountErrors.InvalidState;
        Status = AccountStatus.Frozen;
        return Result.Success();
    }

    public Result Unfreeze()
    {
        if (Status != AccountStatus.Frozen) return AccountErrors.InvalidState;
        Status = AccountStatus.Active;
        return Result.Success();
    }

    public Result Close()
    {
        if (Status == AccountStatus.Closed) return AccountErrors.InvalidState;
        if (Balance.LedgerBalance != 0m || Balance.HoldTotal != 0m) return AccountErrors.NotEmpty;
        Status = AccountStatus.Closed;
        return Result.Success();
    }
}
