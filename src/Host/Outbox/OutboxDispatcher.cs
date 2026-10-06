using BankAccounting.BuildingBlocks;
using BankAccounting.Host.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BankAccounting.Host.Outbox;

/// <summary>Where committed events go. Phase 1 logs them; swap for RabbitMQ/SQS/in-process handlers later.</summary>
public interface IOutboxPublisher
{
    Task PublishAsync(string eventType, string payloadJson, CancellationToken ct);
}

public sealed class LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> log) : IOutboxPublisher
{
    public Task PublishAsync(string eventType, string payloadJson, CancellationToken ct)
    {
        log.LogInformation("Domain event {EventType}: {Payload}", eventType, payloadJson);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Transactional-outbox relay. FOR UPDATE SKIP LOCKED makes it safe to run on several instances
/// (and no separate scheduler/lock is needed).
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IClock clock, ILogger<OutboxDispatcher> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DispatchBatchAsync(stoppingToken);
                if (processed == 0) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox dispatch failed; retrying shortly.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.Outbox
            .FromSql($"""
                SELECT * FROM txn.outbox_message
                WHERE processed_at IS NULL
                ORDER BY created_at, id
                LIMIT 50
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message.EventType, message.Payload, ct);
                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception ex)
            {
                message.MarkFailed(ex.Message); // stays pending; add a dead-letter threshold in a later increment
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}
