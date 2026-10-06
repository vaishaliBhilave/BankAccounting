using BankAccounting.BuildingBlocks;

namespace BankAccounting.Ledger.Domain;

public enum EntryType : short { Normal = 1, Accrual = 2, Revaluation = 3, Adjustment = 4, Closing = 5, Reversal = 6 }

/// <summary>
/// Append-only. Corrections are new reversing entries, never updates. The database also enforces
/// immutability and balance (deferred constraint trigger) as a safety net.
/// </summary>
public sealed class JournalEntry
{
    private readonly List<JournalLine> _lines = new();

    public long Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public int PeriodId { get; private set; }
    public EntryType EntryType { get; private set; }
    public long? ReversesEntryId { get; private set; }
    public string? Memo { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<JournalLine> Lines => _lines;

    private JournalEntry() { } // EF Core

    public static Result<JournalEntry> Post(Guid transactionId, DateOnly postingDate, int periodId,
        EntryType entryType, DateTimeOffset now, string? memo, IEnumerable<JournalLine> lines)
    {
        var list = lines.ToList();
        if (list.Count < 2) return LedgerErrors.TooFewLines;

        // Every currency must balance on its own; base-currency totals must balance too.
        foreach (var group in list.GroupBy(l => l.CurrencyCode))
            if (group.Sum(l => l.DebitAmount) != group.Sum(l => l.CreditAmount))
                return LedgerErrors.Unbalanced;
        if (list.Sum(l => l.BaseDebit) != list.Sum(l => l.BaseCredit))
            return LedgerErrors.Unbalanced;

        var entry = new JournalEntry
        {
            TransactionId = transactionId, PostingDate = postingDate, PeriodId = periodId,
            EntryType = entryType, Memo = memo, CreatedAt = now
        };
        entry._lines.AddRange(list);
        return entry;
    }
}
