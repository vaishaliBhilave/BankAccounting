namespace BankAccounting.Ledger.Domain;

public enum PeriodStatus : short { Future = 0, Open = 1, SoftClosed = 2, Closed = 3, Locked = 4 }

public sealed class AccountingPeriod
{
    public int Id { get; private set; }
    public int FiscalYearId { get; private set; }
    public short PeriodNo { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public PeriodStatus Status { get; private set; }

    private AccountingPeriod() { } // EF Core (close workflow is added in a later increment)

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}
