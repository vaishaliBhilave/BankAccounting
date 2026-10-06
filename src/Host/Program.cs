using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BankAccounting.BuildingBlocks;
using BankAccounting.Host;
using BankAccounting.Host.Auth;
using BankAccounting.Host.Endpoints;
using BankAccounting.Host.Outbox;
using BankAccounting.Host.Persistence;
using BankAccounting.Transactions.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- configuration (fail fast) ----------
var connectionString = config.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is required (env var ConnectionStrings__Default).");

var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters (env var Jwt__Key).");

// ---------- persistence ----------
builder.Services.AddDbContext<BankDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddScoped<ITransactionStore, EfTransactionStore>();
builder.Services.AddScoped<IAccountStore, EfAccountStore>();
builder.Services.AddScoped<ILedgerStore, EfLedgerStore>();
builder.Services.AddScoped<IAuditTrail, EfAuditTrail>();
builder.Services.AddScoped<IOutbox, EfOutbox>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<PostTransactionHandler>();

// ---------- cross-cutting ----------
var zone = TimeZoneInfo.FindSystemTimeZoneById(config["Bank:BusinessTimeZone"] ?? "Asia/Kolkata");
builder.Services.AddSingleton<IClock>(new SystemClock(zone));
builder.Services.AddSingleton(new PostingOptions(config["Bank:BaseCurrency"] ?? "INR"));

// Cache: in-memory L1 always; Redis L2 only when configured. The app must work without Redis.
var redis = config.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis))
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis);
builder.Services.AddHybridCache();

// ---------- outbox ----------
builder.Services.AddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();

// ---------- auth ----------
builder.Services.Configure<JwtOptions>(config.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = "name",
        RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization();

// ---------- rate limiting (public showcase) ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Ip(c),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// ---------- startup: schema + demo data ----------
if (config.GetValue("Database:MigrateOnStartup", true))
    DatabaseMigrator.Run(connectionString);

if (config.GetValue("Seed:DemoUsers", false))
{
    var demoPassword = config["Seed:DemoPassword"];
    if (string.IsNullOrWhiteSpace(demoPassword))
        throw new InvalidOperationException("Seed:DemoPassword is required when Seed:DemoUsers is true.");
    await DemoDataSeeder.SeedAsync(app.Services, demoPassword);
}

// ---------- pipeline ----------
// Behind a TLS-terminating proxy (free PaaS), set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (done in the Dockerfile).
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/health/ready", async (BankDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503)).AllowAnonymous();

app.MapAuth();
app.MapCustomers();
app.MapAccounts();
app.MapTransactions();

// Serve the React build (copied to wwwroot) when present.
if (app.Environment.WebRootPath is { } webRoot && Directory.Exists(webRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();
