using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Transactions.Application;
using BankAccounting.Transactions.Domain;
using Xunit;

namespace BankAccounting.Domain.Tests;

public class TransactionPostingTests
{
    private static readonly Guid User = Guid.NewGuid();

    private readonly FakeStore _db = new();
    private readonly Account _a;   // funded with 1000.00 INR
    private readonly Account _b;   // empty
    private readonly PostTransactionHandler _handler;

    public TransactionPostingTests()
    {
        _a = NewAccount("BA1000000001", 1000m);
        _b = NewAccount("BA1000000002", 0m);
        _handler = new PostTransactionHandler(_db, _db, _db, _db, _db, _db, new FakeClock(), new PostingOptions("INR"));
    }

    private Account NewAccount(string number, decimal opening)
    {
        var acc = Account.Open(Guid.NewGuid(), number, Guid.NewGuid(), 2100, "INR", DateTimeOffset.UtcNow);
        if (opening > 0) acc.Balance.Credit(Money.Of(opening, "INR"));
        _db.Accounts[acc.Id] = acc;
        return acc;
    }

    private PostTransactionCommand Transfer(decimal amount, string key = "key-0001", string? description = null) =>
        new(PostingKind.Transfer, key, User, _a.Id, _b.Id, amount, "INR", description);

    [Fact]
    public async Task Transfer_moves_money_and_writes_a_balanced_entry()
    {
        var result = await _handler.HandleAsync(Transfer(250m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Replayed);
        Assert.Equal(750m, _a.Balance.LedgerBalance);
        Assert.Equal(250m, _b.Balance.LedgerBalance);

        var entry = Assert.Single(_db.Entries);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(entry.Lines.Sum(l => l.DebitAmount), entry.Lines.Sum(l => l.CreditAmount));
        Assert.Single(_db.Transactions);
        Assert.Single(_db.OutboxEvents);
        Assert.Single(_db.AuditActions);
    }

    [Fact]
    public async Task Insufficient_funds_changes_nothing()
    {
        var result = await _handler.HandleAsync(Transfer(1000.01m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("INSUFFICIENT_FUNDS", result.Error.Code);
        Assert.Equal(1000m, _a.Balance.LedgerBalance);
        Assert.Equal(0m, _b.Balance.LedgerBalance);
        Assert.Empty(_db.Entries);
        Assert.Empty(_db.Transactions);
        Assert.Empty(_db.OutboxEvents);
    }

    [Fact]
    public async Task Replaying_the_same_request_returns_the_original_and_posts_once()
    {
        var first = await _handler.HandleAsync(Transfer(100m), CancellationToken.None);
        var second = await _handler.HandleAsync(Transfer(100m), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.Replayed);
        Assert.Equal(first.Value.TransactionId, second.Value.TransactionId);
        Assert.Equal(900m, _a.Balance.LedgerBalance);
        Assert.Single(_db.Entries);
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_payload_is_a_conflict()
    {
        await _handler.HandleAsync(Transfer(100m), CancellationToken.None);
        var second = await _handler.HandleAsync(Transfer(200m), CancellationToken.None);

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", second.Error.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal(900m, _a.Balance.LedgerBalance);
    }

    [Fact]
    public async Task Losing_the_idempotency_race_returns_the_winners_result()
    {
        _db.OnCommit = _db.CommitPendingAsWinnerAndThrowDuplicate;

        var result = await _handler.HandleAsync(Transfer(100m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Replayed);
        Assert.Single(_db.Transactions);
    }

    [Fact]
    public async Task Deposit_debits_cash_and_credits_the_customer()
    {
        var cmd = new PostTransactionCommand(PostingKind.Deposit, "dep-0001", User, null, _b.Id, 500m, "INR", "cash in");
        var result = await _handler.HandleAsync(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, _b.Balance.LedgerBalance);
        var entry = Assert.Single(_db.Entries);
        Assert.Equal(1000L, entry.Lines.Single(l => l.DebitAmount > 0).LedgerAccountId); // Dr Cash
        Assert.Equal(_b.Id, entry.Lines.Single(l => l.CreditAmount > 0).AccountId);       // Cr customer
    }

    [Fact]
    public async Task Withdrawal_cannot_overdraw()
    {
        var cmd = new PostTransactionCommand(PostingKind.Withdrawal, "wd-0001", User, _b.Id, null, 1m, "INR", null);
        var result = await _handler.HandleAsync(cmd, CancellationToken.None);

        Assert.Equal("INSUFFICIENT_FUNDS", result.Error.Code);
    }

    [Fact]
    public async Task Frozen_source_is_rejected_but_frozen_destination_is_allowed()
    {
        _b.Freeze();
        Assert.True((await _handler.HandleAsync(Transfer(10m, "k-in"), CancellationToken.None)).IsSuccess);

        var back = new PostTransactionCommand(PostingKind.Transfer, "k-out", User, _b.Id, _a.Id, 5m, "INR", null);
        Assert.Equal("ACCOUNT_FROZEN", (await _handler.HandleAsync(back, CancellationToken.None)).Error.Code);
    }

    [Fact]
    public async Task Posting_is_blocked_when_no_period_is_open()
    {
        _db.OpenPeriodId = null;
        var result = await _handler.HandleAsync(Transfer(10m), CancellationToken.None);

        Assert.Equal("PERIOD_CLOSED", result.Error.Code);
        Assert.Equal(1000m, _a.Balance.LedgerBalance);
    }

    [Fact]
    public async Task Request_validation_rejects_bad_input()
    {
        Assert.Equal("ACCOUNTS_INVALID", (await _handler.HandleAsync(
            new(PostingKind.Transfer, "k-same-1", User, _a.Id, _a.Id, 1m, "INR", null), CancellationToken.None)).Error.Code);
        Assert.Equal("INVALID_AMOUNT", (await _handler.HandleAsync(Transfer(0m, "k-zero-01"), CancellationToken.None)).Error.Code);
        Assert.Equal("INVALID_AMOUNT", (await _handler.HandleAsync(Transfer(1.234m, "k-dec-001"), CancellationToken.None)).Error.Code);
        Assert.Equal("IDEMPOTENCY_KEY_INVALID", (await _handler.HandleAsync(Transfer(1m, ""), CancellationToken.None)).Error.Code);
        Assert.Equal("CURRENCY_NOT_SUPPORTED", (await _handler.HandleAsync(
            new(PostingKind.Transfer, "k-usd-001", User, _a.Id, _b.Id, 1m, "USD", null), CancellationToken.None)).Error.Code);
        Assert.Equal("ACCOUNT_NOT_FOUND", (await _handler.HandleAsync(
            new(PostingKind.Transfer, "k-nf-0001", User, _a.Id, Guid.NewGuid(), 1m, "INR", null), CancellationToken.None)).Error.Code);
    }
}
