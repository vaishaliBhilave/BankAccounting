namespace BankAccounting.Host.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = "";
    public string Payload { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(string eventType, string payloadJson, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), EventType = eventType, Payload = payloadJson, CreatedAt = now };

    public void MarkProcessed(DateTimeOffset now) { ProcessedAt = now; Attempts++; LastError = null; }
    public void MarkFailed(string error) { Attempts++; LastError = error.Length > 500 ? error[..500] : error; }
}

public sealed class AuditLogEntry
{
    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = "";
    public string EntityType { get; private set; } = "";
    public Guid EntityId { get; private set; }
    public string? Data { get; private set; }

    private AuditLogEntry() { }

    public static AuditLogEntry Create(DateTimeOffset now, Guid? actorId, string action, string entityType, Guid entityId, string? dataJson) =>
        new() { OccurredAt = now, ActorId = actorId, Action = action, EntityType = entityType, EntityId = entityId, Data = dataJson };
}
