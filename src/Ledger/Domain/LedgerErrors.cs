using BankAccounting.BuildingBlocks;

namespace BankAccounting.Ledger.Domain;

public static class LedgerErrors
{
    public static readonly Error Unbalanced = Error.Unprocessable("UNBALANCED_ENTRY", "Journal entry debits and credits do not balance.");
    public static readonly Error TooFewLines = Error.Unprocessable("ENTRY_TOO_FEW_LINES", "A journal entry needs at least two lines.");
    public static readonly Error PeriodClosed = Error.Unprocessable("PERIOD_CLOSED", "No open accounting period exists for the posting date.");
}
