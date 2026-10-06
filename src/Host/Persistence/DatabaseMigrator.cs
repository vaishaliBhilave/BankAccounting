using DbUp;

namespace BankAccounting.Host.Persistence;

public static class DatabaseMigrator
{
    /// <summary>Applies db/migrations/*.sql (embedded) exactly once each, in filename order, one transaction per script.</summary>
    public static void Run(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly,
                name => name.StartsWith("migrations.", StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException("Database migration failed.", result.Error);
    }
}
