namespace BankAccounting.Host.Auth;

public sealed class AppUser
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public string Role { get; private set; } = "";
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AppUser() { }

    public static AppUser Create(string username, string passwordHash, string role, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), Username = username, PasswordHash = passwordHash, Role = role, IsActive = true, CreatedAt = now };
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Teller = "Teller";
    public const string Auditor = "Auditor";
}
