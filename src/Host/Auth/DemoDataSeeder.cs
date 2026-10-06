using BankAccounting.Host.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BankAccounting.Host.Auth;

/// <summary>Creates admin/teller/auditor demo users when Seed:DemoUsers=true. Showcase use only.</summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, string password, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        if (await db.Users.AnyAsync(ct)) return;

        var hasher = new PasswordHasher<AppUser>();
        var now = DateTimeOffset.UtcNow;
        foreach (var (username, role) in new[] { ("admin", Roles.Admin), ("teller", Roles.Teller), ("auditor", Roles.Auditor) })
        {
            var placeholder = AppUser.Create(username, "", role, now);
            db.Users.Add(AppUser.Create(username, hasher.HashPassword(placeholder, password), role, now));
        }
        await db.SaveChangesAsync(ct);
    }
}
