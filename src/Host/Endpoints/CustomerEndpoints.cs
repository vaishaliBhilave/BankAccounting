using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Host.Auth;
using BankAccounting.Host.Persistence;
using BankAccounting.Transactions.Application;
using Microsoft.EntityFrameworkCore;

namespace BankAccounting.Host.Endpoints;

public sealed record CreateCustomerRequest(string? FullName, string? Email);
public sealed record CustomerDto(Guid Id, string FullName, string? Email, DateTimeOffset CreatedAt);

public static class CustomerEndpoints
{
    public static void MapCustomers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers");

        group.MapPost("", async (CreateCustomerRequest req, HttpContext http, BankDbContext db, IClock clock,
            IAuditTrail audit, CancellationToken ct) =>
        {
            var created = Customer.Create(req.FullName, req.Email, clock.UtcNow);
            if (created.IsFailure) return ApiResults.Problem(created.Error);

            var customer = created.Value;
            db.Customers.Add(customer);
            audit.Record(http.UserId(), "customer.created", "customer", customer.Id);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/customers/{customer.Id}",
                new CustomerDto(customer.Id, customer.FullName, customer.Email, customer.CreatedAt));
        })
        .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Teller));

        group.MapGet("", async (string? q, BankDbContext db, CancellationToken ct) =>
        {
            var query = db.Customers.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().Contains(term));
            }
            var customers = await query
                .OrderByDescending(c => c.CreatedAt)
                .Take(50)
                .Select(c => new CustomerDto(c.Id, c.FullName, c.Email, c.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(customers);
        })
        .RequireAuthorization();

        group.MapGet("{id:guid}", async (Guid id, BankDbContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CustomerDto(c.Id, c.FullName, c.Email, c.CreatedAt))
                .FirstOrDefaultAsync(ct);
            return customer is null
                ? ApiResults.Problem(Error.NotFound("CUSTOMER_NOT_FOUND", "Customer not found."))
                : Results.Ok(customer);
        })
        .RequireAuthorization();
    }
}
