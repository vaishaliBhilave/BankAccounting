namespace BankAccounting.BuildingBlocks;

/// <summary>
/// Immutable amount + ISO-4217 currency, always rounded to the currency's minor units.
/// Never use float/double for money; this wraps System.Decimal (maps to numeric(19,4) in Postgres).
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency) => (Amount, Currency) = (amount, currency);

    public static Money Of(decimal amount, string currency)
    {
        var ccy = NormalizeCurrency(currency);
        return new Money(Round(amount, ccy), ccy);
    }

    public static Money Zero(string currency) => Of(0m, currency);

    public bool IsPositive => Amount > 0m;
    public bool IsZero => Amount == 0m;

    public static string NormalizeCurrency(string currency)
    {
        var c = currency?.Trim();
        if (string.IsNullOrEmpty(c) || c.Length != 3 || !c.All(char.IsLetter))
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        return c.ToUpperInvariant();
    }

    /// <summary>Decimal places for a currency (extend as you add currencies).</summary>
    public static int MinorUnits(string currency) => currency.ToUpperInvariant() switch
    {
        "JPY" or "KRW" or "VND" => 0,
        "KWD" or "BHD" or "OMR" or "JOD" or "TND" => 3,
        _ => 2
    };

    /// <summary>Rounding policy: half away from zero (round-half-up for positives). Change here only.</summary>
    public static decimal Round(decimal amount, string currency) =>
        Math.Round(amount, MinorUnits(currency), MidpointRounding.AwayFromZero);

    public static Money operator +(Money a, Money b) { EnsureSame(a, b); return new Money(a.Amount + b.Amount, a.Currency); }
    public static Money operator -(Money a, Money b) { EnsureSame(a, b); return new Money(a.Amount - b.Amount, a.Currency); }
    public static bool operator >(Money a, Money b) { EnsureSame(a, b); return a.Amount > b.Amount; }
    public static bool operator <(Money a, Money b) { EnsureSame(a, b); return a.Amount < b.Amount; }
    public static bool operator >=(Money a, Money b) { EnsureSame(a, b); return a.Amount >= b.Amount; }
    public static bool operator <=(Money a, Money b) { EnsureSame(a, b); return a.Amount <= b.Amount; }

    private static void EnsureSame(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}.");
    }

    public override string ToString() => $"{Amount} {Currency}";
}
