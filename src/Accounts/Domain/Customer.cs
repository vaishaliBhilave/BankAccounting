using BankAccounting.BuildingBlocks;

namespace BankAccounting.Accounts.Domain;

public sealed class Customer
{
    public Guid Id { get; private set; }
    public string FullName { get; private set; } = "";
    public string? Email { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Customer() { } // EF Core

    public static Result<Customer> Create(string? fullName, string? email, DateTimeOffset now)
    {
        var name = fullName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
            return Error.Validation("CUSTOMER_NAME_INVALID", "Full name is required (max 200 characters).");

        var mail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (mail is not null && (mail.Length > 254 || !mail.Contains('@')))
            return Error.Validation("CUSTOMER_EMAIL_INVALID", "Email address is not valid.");

        return new Customer { Id = Guid.CreateVersion7(), FullName = name, Email = mail, CreatedAt = now };
    }
}
