namespace BankAccounting.BuildingBlocks;

/// <summary>Marker for events published through the transactional outbox.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>The bank's business date (calendar date in the configured business time zone).</summary>
    DateOnly BusinessDate { get; }
}

public sealed class SystemClock(TimeZoneInfo businessZone) : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly BusinessDate =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, businessZone).DateTime);
}
