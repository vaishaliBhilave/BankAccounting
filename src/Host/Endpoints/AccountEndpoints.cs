using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Host.Auth;
using BankAccounting.Host.Persistence;
using BankAccounting.Ledger.Domain;
using BankAccounting.Transactions.Application;
using Microsoft.EntityFrameworkCore;

namespace BankAccounting.Host.Endpoints;

public sealed record OpenAccountRequest(Guid CustomerId, string? Product);
public sealed record AccountDto(Guid Id, string AccountNumber, Guid CustomerId, string Currency, AccountStatus Status,
    decimal LedgerBalance, decimal HoldTotal, decimal AvailableBalance);
public sealed record AccountSummaryDto(Guid Id, string AccountNumber, Guid CustomerId, string CustomerName, string Currency,
    AccountStatus Status, decimal LedgerBalance, decimal AvailableBalance);
public sealed record StatementRowDto(Guid TransactionId, string Type, string? Description, DateOnly PostingDate,
    DateTimeOffset CreatedAt, string Direction, decimal Amount, string Currency, string? CounterpartyNumber);
public sealed record BalanceDto(Guid AccountId, string Currency, decimal LedgerBalance, decimal HoldTotal, decimal AvailableBalance);

public static class AccountEndpoints
{
    public static void MapAccounts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/accounts");

        group.MapPost("", async (OpenAccountRequest req, HttpContext http, BankDbContext db, IClock clock,
            PostingOptions options, IAuditTrail audit, CancellationToken ct) =>
        {
            var glCode = req.Product?.Trim().ToLowerInvariant() switch
            {
                "savings" => SystemLedgerCodes.SavingsDeposits,
                "current" => SystemLedgerCodes.CurrentDeposits,
                _ => null
            };
            if (glCode is null)
                return ApiResults.Problem(Error.Validation("PRODUCT_INVALID", "Product must be 'savings' or 'current'."));

            if (!await db.Customers.AnyAsync(c => c.Id == req.CustomerId, ct))
                return ApiResults.Problem(Error.NotFound("CUSTOMER_NOT_FOUND", "Customer not found."));

            var ledgerAccountId = await db.LedgerAccounts.Where(l => l.Code == glCode).Select(l => l.Id).SingleAsync(ct);
            var sequence = await db.Database.SqlQuery<long>($"SELECT nextval('acct.account_number_seq') AS \"Value\"").SingleAsync(ct);

            var account = Account.Open(Guid.CreateVersion7(), $"BA{sequence}", req.CustomerId, ledgerAccountId,
                options.BaseCurrency, clock.UtcNow);
            db.Accounts.Add(account);
            audit.Record(http.UserId(), "account.opened", "account", account.Id, new { req.CustomerId, product = glCode });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/accounts/{account.Id}", ToDto(account));
        })
        .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Teller));

        // Lookup/list for the UI: ?q= matches account number or holder name; ?customerId= narrows to one customer.
        group.MapGet("", async (string? q, Guid? customerId, BankDbContext db, CancellationToken ct) =>
        {
            var query = from a in db.Accounts.AsNoTracking()
                        join c in db.Customers.AsNoTracking() on a.CustomerId equals c.Id
                        select new { a, c };
            if (customerId is { } cid) query = query.Where(x => x.a.CustomerId == cid);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(x => x.a.AccountNumber.ToLower().Contains(term) || x.c.FullName.ToLower().Contains(term));
            }
            var rows = await query
                .OrderByDescending(x => x.a.OpenedAt)
                .Take(50)
                .Select(x => new AccountSummaryDto(x.a.Id, x.a.AccountNumber, x.a.CustomerId, x.c.FullName, x.a.CurrencyCode,
                    x.a.Status, x.a.Balance.LedgerBalance, x.a.Balance.LedgerBalance - x.a.Balance.HoldTotal))
                .ToListAsync(ct);
            return Results.Ok(rows);
        })
        .RequireAuthorization();

        // Passbook-style statement: newest first, from this account's point of view (Debit = money out).
        group.MapGet("{id:guid}/transactions", async (Guid id, int? limit, BankDbContext db, CancellationToken ct) =>
        {
            if (!await db.Accounts.AnyAsync(a => a.Id == id, ct)) return ApiResults.Problem(AccountErrors.NotFound);

            var rows = await (
                from t in db.Transactions.AsNoTracking()
                where t.FromAccountId == id || t.ToAccountId == id
                orderby t.CreatedAt descending, t.Id descending
                select new
                {
                    t.Id, t.Type, t.Description, t.PostingDate, t.CreatedAt, t.Amount, t.CurrencyCode,
                    IsDebit = t.FromAccountId == id,
                    FromNumber = db.Accounts.Where(a => a.Id == t.FromAccountId).Select(a => a.AccountNumber).FirstOrDefault(),
                    ToNumber = db.Accounts.Where(a => a.Id == t.ToAccountId).Select(a => a.AccountNumber).FirstOrDefault()
                })
                .Take(Math.Clamp(limit ?? 50, 1, 200))
                .ToListAsync(ct);

            return Results.Ok(rows.Select(r => new StatementRowDto(r.Id, r.Type.ToString(), r.Description, r.PostingDate,
                r.CreatedAt, r.IsDebit ? "Debit" : "Credit", r.Amount, r.CurrencyCode,
                r.IsDebit ? r.ToNumber : r.FromNumber)));
        })
        .RequireAuthorization();

        group.MapGet("{id:guid}", async (Guid id, BankDbContext db, CancellationToken ct) =>
        {
            var account = await db.Accounts.AsNoTracking().Include(a => a.Balance).FirstOrDefaultAsync(a => a.Id == id, ct);
            return account is null ? ApiResults.Problem(AccountErrors.NotFound) : Results.Ok(ToDto(account));
        })
        .RequireAuthorization();

        group.MapGet("{id:guid}/balance", async (Guid id, BankDbContext db, CancellationToken ct) =>
        {
            var balance = await db.Balances.AsNoTracking().Where(b => b.AccountId == id).FirstOrDefaultAsync(ct);
            return balance is null
                ? ApiResults.Problem(AccountErrors.NotFound)
                : Results.Ok(new BalanceDto(balance.AccountId, balance.CurrencyCode, balance.LedgerBalance,
                    balance.HoldTotal, balance.AvailableBalance));
        })
        .RequireAuthorization();

        group.MapPost("{id:guid}/freeze", (Guid id, HttpContext http, BankDbContext db, IAuditTrail audit, CancellationToken ct) =>
            ChangeStatusAsync(id, http, db, audit, a => a.Freeze(), "account.frozen", ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        group.MapPost("{id:guid}/unfreeze", (Guid id, HttpContext http, BankDbContext db, IAuditTrail audit, CancellationToken ct) =>
            ChangeStatusAsync(id, http, db, audit, a => a.Unfreeze(), "account.unfrozen", ct))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }

    private static async Task<IResult> ChangeStatusAsync(Guid id, HttpContext http, BankDbContext db, IAuditTrail audit,
        Func<Account, Result> change, string auditAction, CancellationToken ct)
    {
        var account = await db.Accounts.Include(a => a.Balance).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return ApiResults.Problem(AccountErrors.NotFound);

        var result = change(account);
        if (result.IsFailure) return ApiResults.Problem(result.Error);

        audit.Record(http.UserId(), auditAction, "account", account.Id);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(account));
    }

    private static AccountDto ToDto(Account a) => new(a.Id, a.AccountNumber, a.CustomerId, a.CurrencyCode, a.Status,
        a.Balance.LedgerBalance, a.Balance.HoldTotal, a.Balance.AvailableBalance);
}
