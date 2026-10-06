using BankAccounting.BuildingBlocks;
using BankAccounting.Host.Auth;
using BankAccounting.Host.Persistence;
using BankAccounting.Transactions.Application;
using BankAccounting.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace BankAccounting.Host.Endpoints;

public sealed record DepositRequest(Guid AccountId, decimal Amount, string? Currency, string? Description);
public sealed record WithdrawalRequest(Guid AccountId, decimal Amount, string? Currency, string? Description);
public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, string? Currency, string? Description);

public sealed record TransactionDto(Guid Id, TransactionType Type, TransactionStatus Status, Guid? FromAccountId,
    Guid? ToAccountId, decimal Amount, string Currency, string? Description, DateOnly PostingDate, DateTimeOffset CreatedAt);

public static class TransactionEndpoints
{
    public static void MapTransactions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transactions");

        // Every posting endpoint requires an Idempotency-Key header.
        group.MapPost("deposits", (DepositRequest r, HttpContext http, PostTransactionHandler h, CancellationToken ct) =>
                ExecuteAsync(http, h, PostingKind.Deposit, null, r.AccountId, r.Amount, r.Currency, r.Description, ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Teller));

        group.MapPost("withdrawals", (WithdrawalRequest r, HttpContext http, PostTransactionHandler h, CancellationToken ct) =>
                ExecuteAsync(http, h, PostingKind.Withdrawal, r.AccountId, null, r.Amount, r.Currency, r.Description, ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Teller));

        group.MapPost("transfers", (TransferRequest r, HttpContext http, PostTransactionHandler h, CancellationToken ct) =>
                ExecuteAsync(http, h, PostingKind.Transfer, r.FromAccountId, r.ToAccountId, r.Amount, r.Currency, r.Description, ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Teller));

        group.MapGet("{id:guid}", async (Guid id, BankDbContext db, CancellationToken ct) =>
        {
            var dto = await db.Transactions.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new TransactionDto(t.Id, t.Type, t.Status, t.FromAccountId, t.ToAccountId, t.Amount,
                    t.CurrencyCode, t.Description, t.PostingDate, t.CreatedAt))
                .FirstOrDefaultAsync(ct);
            return dto is null
                ? ApiResults.Problem(Error.NotFound("TRANSACTION_NOT_FOUND", "Transaction not found."))
                : Results.Ok(dto);
        })
        .RequireAuthorization();
    }

    private static async Task<IResult> ExecuteAsync(HttpContext http, PostTransactionHandler handler, PostingKind kind,
        Guid? from, Guid? to, decimal amount, string? currency, string? description, CancellationToken ct)
    {
        if (http.UserId() is not { } userId) return Results.Unauthorized();

        var key = http.Request.Headers["Idempotency-Key"].ToString();
        var result = await handler.HandleAsync(
            new PostTransactionCommand(kind, key, userId, from, to, amount, currency ?? "", description), ct);

        if (result.IsFailure) return ApiResults.Problem(result.Error);

        var receipt = result.Value;
        http.Response.Headers["Idempotent-Replayed"] = receipt.Replayed ? "true" : "false";
        return receipt.Replayed
            ? Results.Ok(receipt)
            : Results.Created($"/api/v1/transactions/{receipt.TransactionId}", receipt);
    }
}
