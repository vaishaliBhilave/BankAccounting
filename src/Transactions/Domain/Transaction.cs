using BankAccounting.BuildingBlocks;

namespace BankAccounting.Transactions.Domain;

public enum TransactionType : short { Deposit = 1, Withdrawal = 2, Transfer = 3, Fee = 4, Reversal = 5 }
public enum TransactionStatus : short { Pending = 1, Posted = 2, Reversed = 3, Failed = 4 }

/// <summary>The business-level record of a money movement; its accounting lives in a JournalEntry.</summary>
public sealed class Transaction
{
    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string RequestHash { get; private set; } = "";
    public TransactionType Type { get; private set; }
    public TransactionStatus Status { get; private set; }
    public Guid? FromAccountId { get; private set; }
    public Guid? ToAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public string? Description { get; private set; }
    public Guid InitiatedBy { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? ReversalOfId { get; private set; }

    private Transaction() { } // EF Core

    public static Transaction Posted(string idempotencyKey, string requestHash, TransactionType type,
        Guid? fromAccountId, Guid? toAccountId, Money amount, string? description, Guid initiatedBy,
        DateOnly postingDate, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        IdempotencyKey = idempotencyKey,
        RequestHash = requestHash,
        Type = type,
        Status = TransactionStatus.Posted,
        FromAccountId = fromAccountId,
        ToAccountId = toAccountId,
        Amount = amount.Amount,
        CurrencyCode = amount.Currency,
        Description = description,
        InitiatedBy = initiatedBy,
        PostingDate = postingDate,
        CreatedAt = now
    };
}

public sealed record TransactionPosted(
    Guid TransactionId, TransactionType Type, Guid? FromAccountId, Guid? ToAccountId,
    decimal Amount, string CurrencyCode, DateOnly PostingDate, DateTimeOffset OccurredAt) : IDomainEvent;
