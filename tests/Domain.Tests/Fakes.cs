using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Ledger.Domain;
using BankAccounting.Transactions.Application;
using BankAccounting.Transactions.Domain;

namespace BankAccounting.Domain.Tests;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow => new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    public DateOnly BusinessDate => new(2026, 10, 1);
}

/// <summary>
/// In-memory implementation of every persistence port. Changes are "pending" until the unit of work
/// commits, so tests can assert that failed postings leave nothing behind.
/// </summary>
internal sealed class FakeStore : ITransactionStore, IAccountStore, ILedgerStore, IAuditTrail, IOutbox, IUnitOfWork
{
    public readonly Dictionary<Guid, Account> Accounts = new();
    public readonly List<Transaction> Transactions = new();
    public readonly List<JournalEntry> Entries = new();
    public readonly List<IDomainEvent> OutboxEvents = new();
    public readonly List<string> AuditActions = new();
    public int? OpenPeriodId = 7;
    public Action? OnCommit;                     // lets a test simulate a lost idempotency race

    private readonly List<Transaction> _pendingTx = new();
    private readonly List<JournalEntry> _pendingEntries = new();
    private readonly List<IDomainEvent> _pendingEvents = new();
    private readonly List<string> _pendingAudit = new();

    // ITransactionStore
    public Task<Transaction?> FindByKeyAsync(Guid initiatedBy, string key, CancellationToken ct) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.InitiatedBy == initiatedBy && t.IdempotencyKey == key));
    public void Add(Transaction transaction) => _pendingTx.Add(transaction);

    // IAccountStore
    public Task<IReadOnlyDictionary<Guid, Account>> LockForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Account>>(
            Accounts.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

    // ILedgerStore
    public Task<long> GetSystemAccountIdAsync(string code, CancellationToken ct) => Task.FromResult(1000L);
    public Task<int?> FindOpenPeriodIdAsync(DateOnly date, CancellationToken ct) => Task.FromResult(OpenPeriodId);
    public void Add(JournalEntry entry) => _pendingEntries.Add(entry);

    // IAuditTrail / IOutbox
    public void Record(Guid? actorId, string action, string entityType, Guid entityId, object? data = null) => _pendingAudit.Add(action);
    public void Enqueue(IDomainEvent domainEvent) => _pendingEvents.Add(domainEvent);

    // IUnitOfWork
    public Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct) => Task.FromResult<IUnitOfWorkScope>(new Scope(this));

    private sealed class Scope(FakeStore s) : IUnitOfWorkScope
    {
        private bool _committed;

        public Task SaveAndCommitAsync(CancellationToken ct)
        {
            if (s.OnCommit is { } hook)
            {
                s.OnCommit = null;
                hook();
            }
            s.Transactions.AddRange(s._pendingTx);
            s.Entries.AddRange(s._pendingEntries);
            s.OutboxEvents.AddRange(s._pendingEvents);
            s.AuditActions.AddRange(s._pendingAudit);
            s.ClearPending();
            _committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed) s.ClearPending();
            return ValueTask.CompletedTask;
        }
    }

    public void ClearPending()
    {
        _pendingTx.Clear(); _pendingEntries.Clear(); _pendingEvents.Clear(); _pendingAudit.Clear();
    }

    /// <summary>Simulate another request committing the same transaction first.</summary>
    public void CommitPendingAsWinnerAndThrowDuplicate()
    {
        Transactions.AddRange(_pendingTx);
        ClearPending();
        throw new DuplicateIdempotencyKeyException();
    }
}
