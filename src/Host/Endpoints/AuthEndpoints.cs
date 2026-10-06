using BankAccounting.BuildingBlocks;
using BankAccounting.Host.Auth;
using BankAccounting.Host.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BankAccounting.Host.Endpoints;

public sealed record LoginRequest(string? Username, string? Password);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string Role);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/login", async (LoginRequest req, BankDbContext db, TokenService tokens, CancellationToken ct) =>
        {
            var invalid = ApiResults.Problem(new Error("INVALID_CREDENTIALS", "Invalid username or password.", ErrorType.Forbidden));
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrEmpty(req.Password)) return invalid;

            var username = req.Username.Trim().ToLower();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username && u.IsActive, ct);
            if (user is null) return invalid;

            var verdict = new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, req.Password);
            if (verdict == PasswordVerificationResult.Failed) return invalid;

            var (token, expires) = tokens.Issue(user);
            return Results.Ok(new LoginResponse(token, expires, user.Role));
        })
        .AllowAnonymous()
        .RequireRateLimiting("login");
    }
}
