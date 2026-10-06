namespace BankAccounting.Ledger.Domain;

public enum LedgerAccountType : short { Asset = 1, Liability = 2, Equity = 3, Income = 4, Expense = 5 }

/// <summary>Codes of system GL accounts seeded by db/migrations/0003_reference_data.sql.</summary>
public static class SystemLedgerCodes
{
    public const string Cash = "1000";
    public const string SavingsDeposits = "2100";
    public const string CurrentDeposits = "2200";
    public const string InterestPayable = "2300";
    public const string RetainedEarnings = "3000";
    public const string FeeIncome = "4100";
    public const string InterestExpense = "5100";
    public const string Rounding = "5900";
}

/// <summary>Chart-of-accounts node. Customer deposit accounts roll up to a control account here.</summary>
public sealed class LedgerAccount
{
    public long Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public LedgerAccountType Type { get; private set; }
    public long? ParentId { get; private set; }
    public bool IsActive { get; private set; }

    private LedgerAccount() { } // EF Core (read-mostly reference data)
}
