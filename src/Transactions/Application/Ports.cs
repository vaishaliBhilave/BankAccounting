using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Ledger.Domain;
using BankAccounting.Transactions.Domain;

namespace BankAccounting.Transactions.Application;

// Ports implemented by the Host's persistence layer (EF Core + PostgreSQL).
// A single DbContext/DB transaction spans these stores so a posting is atomic across modules.

public interface ITransactionStore
{
    Task<Transaction?> FindByKeyAsync(Guid initiatedBy, string idempotencyKey, CancellationToken ct);
    void Add(Transaction transaction);
}

public interface IAccountStore
{
    /// <summary>
    /// Loads accounts WITH balances and takes row locks (SELECT ... ORDER BY id FOR UPDATE).
    /// Locking in ascending id order is what prevents deadlocks between opposite transfers.
    /// Missing ids are simply absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Account>> LockForUpdateAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct);
}

public interface ILedgerStore
{
    Task<long> GetSystemAccountIdAsync(string code, CancellationToken ct);
    Task<int?> FindOpenPeriodIdAsync(DateOnly postingDate, CancellationToken ct);
    void Add(JournalEntry entry);
}

public interface IAuditTrail
{
    void Record(Guid? actorId, string action, string entityType, Guid entityId, object? data = null);
}

public interface IOutbox
{
    void Enqueue(IDomainEvent domainEvent);
}

public interface IUnitOfWork
{
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct);
}

public interface IUnitOfWorkScope : IAsyncDisposable
{
    /// <summary>Flushes pending changes and commits. Disposing without committing rolls back.</summary>
    Task SaveAndCommitAsync(CancellationToken ct);
}

/// <summary>Thrown by persistence when (initiated_by, idempotency_key) collides: an identical request won the race.</summary>
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException() : base("Idempotency key already used.") { }
}

/// <summary>Thrown by persistence when a database CHECK / constraint trigger rejects the change.</summary>
public sealed class LedgerConstraintViolationException(string constraintName)
    : Exception($"Database constraint violated: {constraintName}")
{
    public string ConstraintName { get; } = constraintName;
}

public sealed record PostingOptions(string BaseCurrency);
