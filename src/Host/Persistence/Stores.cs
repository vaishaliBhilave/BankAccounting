using System.Text.Json;
using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Ledger.Domain;
using BankAccounting.Transactions.Application;
using BankAccounting.Transactions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Npgsql;

namespace BankAccounting.Host.Persistence;

internal sealed class EfTransactionStore(BankDbContext db) : ITransactionStore
{
    public Task<Transaction?> FindByKeyAsync(Guid initiatedBy, string idempotencyKey, CancellationToken ct) =>
        db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.InitiatedBy == initiatedBy && t.IdempotencyKey == idempotencyKey, ct);

    public void Add(Transaction transaction) => db.Transactions.Add(transaction);
}

internal sealed class EfAccountStore(BankDbContext db) : IAccountStore
{
    public async Task<IReadOnlyDictionary<Guid, Account>> LockForUpdateAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken ct)
    {
        var ids = accountIds.ToArray();

        // Row locks, always in ascending id order => opposite transfers can never deadlock.
        await db.Balances
            .FromSql($"SELECT * FROM acct.account_balance WHERE account_id = ANY({ids}) ORDER BY account_id FOR UPDATE")
            .ToListAsync(ct);

        var accounts = await db.Accounts.Include(a => a.Balance).Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        return accounts.ToDictionary(a => a.Id);
    }
}

internal sealed class EfLedgerStore(BankDbContext db, HybridCache cache) : ILedgerStore
{
    // System GL codes never change at runtime, so cache them (Redis-backed L2 when configured).
    public Task<long> GetSystemAccountIdAsync(string code, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"ledger:system-account:{code}",
            async token => await db.LedgerAccounts.Where(l => l.Code == code).Select(l => l.Id).SingleAsync(token),
            cancellationToken: ct).AsTask();

    public Task<int?> FindOpenPeriodIdAsync(DateOnly postingDate, CancellationToken ct) =>
        db.Periods.AsNoTracking()
            .Where(p => p.Status == PeriodStatus.Open && p.StartDate <= postingDate && p.EndDate >= postingDate)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);

    public void Add(JournalEntry entry) => db.JournalEntries.Add(entry);
}

internal sealed class EfAuditTrail(BankDbContext db, IClock clock) : IAuditTrail
{
    public void Record(Guid? actorId, string action, string entityType, Guid entityId, object? data = null) =>
        db.AuditLog.Add(AuditLogEntry.Create(clock.UtcNow, actorId, action, entityType, entityId,
            data is null ? null : JsonSerializer.Serialize(data)));
}

internal sealed class EfOutbox(BankDbContext db, IClock clock) : IOutbox
{
    public void Enqueue(IDomainEvent domainEvent) =>
        db.Outbox.Add(OutboxMessage.Create(domainEvent.GetType().Name,
            JsonSerializer.Serialize(domainEvent, domainEvent.GetType()), clock.UtcNow));
}

internal sealed class EfUnitOfWork(BankDbContext db) : IUnitOfWork
{
    public async Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct) =>
        new Scope(db, await db.Database.BeginTransactionAsync(ct));

    private sealed class Scope(BankDbContext db, IDbContextTransaction tx) : IUnitOfWorkScope
    {
        public async Task SaveAndCommitAsync(CancellationToken ct)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct); // deferred constraint triggers (journal balance) fire here
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
            {
                throw Translate(pg, ex);
            }
            catch (PostgresException pg)
            {
                throw Translate(pg, pg);
            }
        }

        public ValueTask DisposeAsync() => tx.DisposeAsync(); // rolls back if not committed

        private static Exception Translate(PostgresException pg, Exception original) => pg.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation when pg.ConstraintName == "uq_transaction_idempotency"
                => new DuplicateIdempotencyKeyException(),
            PostgresErrorCodes.CheckViolation
                => new LedgerConstraintViolationException(pg.ConstraintName ?? pg.MessageText),
            _ => original
        };
    }
}
