using BankAccounting.BuildingBlocks;

namespace BankAccounting.Ledger.Domain;

public sealed class JournalLine
{
    public long Id { get; private set; }
    public long JournalEntryId { get; private set; }
    public long LedgerAccountId { get; private set; }

    /// <summary>Sub-ledger (customer) account, when the line belongs to one.</summary>
    public Guid? AccountId { get; private set; }

    public string CurrencyCode { get; private set; } = "";
    public decimal DebitAmount { get; private set; }
    public decimal CreditAmount { get; private set; }
    public decimal FxRate { get; private set; } = 1m;
    public decimal BaseDebit { get; private set; }
    public decimal BaseCredit { get; private set; }

    private JournalLine() { } // EF Core

    public static JournalLine Debit(long ledgerAccountId, Guid? accountId, Money amount, Money? baseAmount = null)
    {
        var (rate, @base) = Prepare(amount, baseAmount);
        return new JournalLine
        {
            LedgerAccountId = ledgerAccountId, AccountId = accountId, CurrencyCode = amount.Currency,
            DebitAmount = amount.Amount, CreditAmount = 0m, FxRate = rate, BaseDebit = @base.Amount, BaseCredit = 0m
        };
    }

    public static JournalLine Credit(long ledgerAccountId, Guid? accountId, Money amount, Money? baseAmount = null)
    {
        var (rate, @base) = Prepare(amount, baseAmount);
        return new JournalLine
        {
            LedgerAccountId = ledgerAccountId, AccountId = accountId, CurrencyCode = amount.Currency,
            DebitAmount = 0m, CreditAmount = amount.Amount, FxRate = rate, BaseDebit = 0m, BaseCredit = @base.Amount
        };
    }

    // Programming errors (not business failures) throw. Single-currency release: base == transaction amount.
    private static (decimal Rate, Money Base) Prepare(Money amount, Money? baseAmount)
    {
        if (!amount.IsPositive) throw new ArgumentOutOfRangeException(nameof(amount), "Journal line amounts must be positive.");
        if (baseAmount is null) return (1m, amount);
        if (!baseAmount.Value.IsPositive) throw new ArgumentOutOfRangeException(nameof(baseAmount));
        return (Math.Round(baseAmount.Value.Amount / amount.Amount, 9), baseAmount.Value);
    }
}
