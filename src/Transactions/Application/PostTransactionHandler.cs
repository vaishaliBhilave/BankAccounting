using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Ledger.Domain;
using BankAccounting.Transactions.Domain;

namespace BankAccounting.Transactions.Application;

public enum PostingKind { Deposit, Withdrawal, Transfer }

public sealed record PostTransactionCommand(
    PostingKind Kind,
    string IdempotencyKey,
    Guid InitiatedBy,
    Guid? FromAccountId,
    Guid? ToAccountId,
    decimal Amount,
    string Currency,
    string? Description);

public sealed record PostingReceipt(
    Guid TransactionId, TransactionType Type, TransactionStatus Status,
    DateOnly PostingDate, decimal Amount, string Currency, bool Replayed);

/// <summary>
/// The critical path. Deposits, withdrawals and transfers share one algorithm; they differ only in
/// which two sides of the journal entry are debited/credited.
///   Deposit    : Dr Cash (GL)            / Cr customer account
///   Withdrawal : Dr customer account     / Cr Cash (GL)
///   Transfer   : Dr from-account         / Cr to-account
/// </summary>
public sealed class PostTransactionHandler(
    ITransactionStore transactions,
    IAccountStore accounts,
    ILedgerStore ledger,
    IAuditTrail audit,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    PostingOptions options)
{
    public async Task<Result<PostingReceipt>> HandleAsync(PostTransactionCommand cmd, CancellationToken ct)
    {
        var validation = Validate(cmd, options);
        if (validation.IsFailure) return validation.Error;

        var money = Money.Of(cmd.Amount, cmd.Currency);
        var hash = Fingerprint(cmd, money);

        // 1. Idempotent replay: same key + same payload returns the original result.
        var replay = await TryReplayAsync(cmd.InitiatedBy, cmd.IdempotencyKey, hash, ct);
        if (replay is not null) return replay;

        try
        {
            return await PostAsync(cmd, money, hash, ct);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // Two identical requests raced; the other one committed first. Return its result.
            return await TryReplayAsync(cmd.InitiatedBy, cmd.IdempotencyKey, hash, ct)
                   ?? throw new InvalidOperationException("Idempotency conflict without a stored transaction.");
        }
        catch (LedgerConstraintViolationException ex) when (ex.ConstraintName == "ck_no_negative")
        {
            // Last line of defence tripped (domain check should normally catch this first).
            return AccountErrors.InsufficientFunds;
        }
    }

    private async Task<Result<PostingReceipt>> PostAsync(
        PostTransactionCommand cmd, Money money, string hash, CancellationToken ct)
    {
        await using var scope = await unitOfWork.BeginAsync(ct);

        // 2. Lock every involved balance in ascending id order.
        var ids = new[] { cmd.FromAccountId, cmd.ToAccountId }
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var locked = await accounts.LockForUpdateAsync(ids, ct);

        Account? from = null, to = null;
        if (cmd.FromAccountId is { } fromId && !locked.TryGetValue(fromId, out from))
            return AccountErrors.NotFound;
        if (cmd.ToAccountId is { } toId && !locked.TryGetValue(toId, out to))
            return AccountErrors.NotFound;

        // 3. Business rules.
        if (from is not null)
        {
            var canDebit = from.CanDebit();
            if (canDebit.IsFailure) return canDebit.Error;
            if (from.CurrencyCode != money.Currency) return AccountErrors.CurrencyMismatch;
        }
        if (to is not null)
        {
            var canCredit = to.CanCredit();
            if (canCredit.IsFailure) return canCredit.Error;
            if (to.CurrencyCode != money.Currency) return AccountErrors.CurrencyMismatch;
        }

        var postingDate = clock.BusinessDate;
        var periodId = await ledger.FindOpenPeriodIdAsync(postingDate, ct);
        if (periodId is null) return LedgerErrors.PeriodClosed;

        // 4. Build the balanced journal entry.
        var lines = new List<JournalLine>(2);
        switch (cmd.Kind)
        {
            case PostingKind.Deposit:
            {
                var cash = await ledger.GetSystemAccountIdAsync(SystemLedgerCodes.Cash, ct);
                lines.Add(JournalLine.Debit(cash, null, money));
                lines.Add(JournalLine.Credit(to!.LedgerAccountId, to.Id, money));
                break;
            }
            case PostingKind.Withdrawal:
            {
                var cash = await ledger.GetSystemAccountIdAsync(SystemLedgerCodes.Cash, ct);
                lines.Add(JournalLine.Debit(from!.LedgerAccountId, from.Id, money));
                lines.Add(JournalLine.Credit(cash, null, money));
                break;
            }
            default: // Transfer
                lines.Add(JournalLine.Debit(from!.LedgerAccountId, from.Id, money));
                lines.Add(JournalLine.Credit(to!.LedgerAccountId, to.Id, money));
                break;
        }

        var now = clock.UtcNow;
        var type = cmd.Kind switch
        {
            PostingKind.Deposit => TransactionType.Deposit,
            PostingKind.Withdrawal => TransactionType.Withdrawal,
            _ => TransactionType.Transfer
        };

        var transaction = Transaction.Posted(cmd.IdempotencyKey, hash, type, cmd.FromAccountId, cmd.ToAccountId,
            money, cmd.Description, cmd.InitiatedBy, postingDate, now);

        var entry = JournalEntry.Post(transaction.Id, postingDate, periodId.Value, EntryType.Normal, now,
            cmd.Description, lines);
        if (entry.IsFailure) return entry.Error;

        // 5. Move balances (debit first: it is the only step that can fail after validation).
        if (from is not null)
        {
            var debited = from.Balance.Debit(money);
            if (debited.IsFailure) return debited.Error;
        }
        if (to is not null)
        {
            var credited = to.Balance.Credit(money);
            if (credited.IsFailure) return credited.Error;
        }

        // 6. Persist everything in ONE database transaction: transaction, journal, balances, audit, outbox.
        transactions.Add(transaction);
        ledger.Add(entry.Value);
        audit.Record(cmd.InitiatedBy, "transaction.posted", "transaction", transaction.Id, new
        {
            type = type.ToString(), from = cmd.FromAccountId, to = cmd.ToAccountId,
            amount = money.Amount, currency = money.Currency, postingDate
        });
        outbox.Enqueue(new TransactionPosted(transaction.Id, type, cmd.FromAccountId, cmd.ToAccountId,
            money.Amount, money.Currency, postingDate, now));

        await scope.SaveAndCommitAsync(ct);
        return ToReceipt(transaction, replayed: false);
    }

    private async Task<Result<PostingReceipt>?> TryReplayAsync(
        Guid initiatedBy, string key, string hash, CancellationToken ct)
    {
        var existing = await transactions.FindByKeyAsync(initiatedBy, key, ct);
        if (existing is null) return null;
        if (existing.RequestHash != hash)
            return Error.Conflict("IDEMPOTENCY_KEY_REUSED",
                "This Idempotency-Key was already used with a different request.");
        return ToReceipt(existing, replayed: true);
    }

    private static PostingReceipt ToReceipt(Transaction t, bool replayed) =>
        new(t.Id, t.Type, t.Status, t.PostingDate, t.Amount, t.CurrencyCode, replayed);

    private static Result Validate(PostTransactionCommand c, PostingOptions o)
    {
        if (string.IsNullOrWhiteSpace(c.IdempotencyKey) || c.IdempotencyKey.Length > 100)
            return Error.Validation("IDEMPOTENCY_KEY_INVALID", "Idempotency-Key is required (max 100 characters).");

        switch (c.Kind)
        {
            case PostingKind.Deposit when c.ToAccountId is null || c.FromAccountId is not null:
                return Error.Validation("ACCOUNTS_INVALID", "A deposit needs exactly a destination account.");
            case PostingKind.Withdrawal when c.FromAccountId is null || c.ToAccountId is not null:
                return Error.Validation("ACCOUNTS_INVALID", "A withdrawal needs exactly a source account.");
            case PostingKind.Transfer when c.FromAccountId is null || c.ToAccountId is null:
                return Error.Validation("ACCOUNTS_INVALID", "A transfer needs a source and a destination account.");
            case PostingKind.Transfer when c.FromAccountId == c.ToAccountId:
                return Error.Validation("ACCOUNTS_INVALID", "Source and destination accounts must differ.");
        }

        if (c.Amount <= 0m) return AccountErrors.InvalidAmount;

        string currency;
        try { currency = Money.NormalizeCurrency(c.Currency); }
        catch (ArgumentException) { return Error.Validation("INVALID_CURRENCY", "Currency must be a 3-letter ISO code."); }

        if (Money.Round(c.Amount, currency) != c.Amount)
            return Error.Validation("INVALID_AMOUNT", $"{currency} allows at most {Money.MinorUnits(currency)} decimal places.");

        if (currency != o.BaseCurrency)
            return Error.Validation("CURRENCY_NOT_SUPPORTED", $"Only {o.BaseCurrency} is supported in this release.");

        if (c.Description?.Length > 500)
            return Error.Validation("DESCRIPTION_TOO_LONG", "Description is limited to 500 characters.");

        return Result.Success();
    }

    private static string Fingerprint(PostTransactionCommand c, Money m)
    {
        var canonical = string.Join('|', c.Kind, c.FromAccountId, c.ToAccountId,
            m.Amount.ToString("0.0000", CultureInfo.InvariantCulture), m.Currency, c.Description ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
