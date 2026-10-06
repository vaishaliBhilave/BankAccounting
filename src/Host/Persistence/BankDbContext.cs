using BankAccounting.Accounts.Domain;
using BankAccounting.Host.Auth;
using BankAccounting.Ledger.Domain;
using BankAccounting.Transactions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankAccounting.Host.Persistence;

/// <summary>
/// One DbContext for the whole modular monolith so a posting (transaction + journal + balances + audit + outbox)
/// is a single atomic unit. Module boundaries are kept by project references and architecture tests,
/// and each module owns its own database schema.
/// The schema itself is owned by db/migrations/*.sql (DbUp), not by EF migrations.
/// </summary>
public sealed class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountBalance> Balances => Set<AccountBalance>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<AccountingPeriod> Periods => Set<AccountingPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankDbContext).Assembly);
}

internal sealed class AppUserConfig : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("app_user", "iam");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
    }
}

internal sealed class CustomerConfig : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customer", "acct");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
    }
}

internal sealed class AccountConfig : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("account", "acct");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        b.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.LedgerAccountId);
        b.HasOne(x => x.Balance).WithOne().HasForeignKey<AccountBalance>(x => x.AccountId);
    }
}

internal sealed class AccountBalanceConfig : IEntityTypeConfiguration<AccountBalance>
{
    public void Configure(EntityTypeBuilder<AccountBalance> b)
    {
        b.ToTable("account_balance", "acct");
        b.HasKey(x => x.AccountId);
        b.Property(x => x.AccountId).ValueGeneratedNever();
        b.Ignore(x => x.AvailableBalance); // generated column in the DB; computed property in the domain
    }
}

internal sealed class LedgerAccountConfig : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> b)
    {
        b.ToTable("ledger_account", "ledger");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
    }
}

internal sealed class AccountingPeriodConfig : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> b)
    {
        b.ToTable("accounting_period", "ledger");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
    }
}

internal sealed class JournalEntryConfig : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        b.ToTable("journal_entry", "ledger");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
        b.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransactionId); // lets EF insert the transaction first
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.JournalEntryId);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class JournalLineConfig : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> b)
    {
        b.ToTable("journal_line", "ledger");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
    }
}

internal sealed class TransactionConfig : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transaction", "txn");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
    }
}

internal sealed class OutboxMessageConfig : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_message", "txn");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Payload).HasColumnType("jsonb");
    }
}

internal sealed class AuditLogEntryConfig : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("audit_log", "audit");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
        b.Property(x => x.Data).HasColumnType("jsonb");
    }
}
