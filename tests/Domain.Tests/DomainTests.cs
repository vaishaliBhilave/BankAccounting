using BankAccounting.Accounts.Domain;
using BankAccounting.BuildingBlocks;
using BankAccounting.Ledger.Domain;
using Xunit;

namespace BankAccounting.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Rounds_to_currency_minor_units_half_away_from_zero()
    {
        Assert.Equal(10.13m, Money.Of(10.125m, "INR").Amount);
        Assert.Equal(11m, Money.Of(10.5m, "JPY").Amount);
        Assert.Equal(1.235m, Money.Of(1.2345m, "KWD").Amount);
    }

    [Fact]
    public void Adding_different_currencies_is_a_programming_error()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = Money.Of(1, "INR") + Money.Of(1, "USD"); });
    }

    [Fact]
    public void Rejects_malformed_currency_codes()
    {
        Assert.Throws<ArgumentException>(() => Money.Of(1, "RUPEE"));
        Assert.Throws<ArgumentException>(() => Money.Of(1, ""));
    }
}

public class JournalEntryTests
{
    private static readonly DateOnly Date = new(2026, 10, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Balanced_entry_posts()
    {
        var result = JournalEntry.Post(Guid.NewGuid(), Date, 7, EntryType.Normal, Now, null, new[]
        {
            JournalLine.Debit(1, null, Money.Of(100, "INR")),
            JournalLine.Credit(2, Guid.NewGuid(), Money.Of(100, "INR"))
        });
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    [Fact]
    public void Unbalanced_entry_is_rejected()
    {
        var result = JournalEntry.Post(Guid.NewGuid(), Date, 7, EntryType.Normal, Now, null, new[]
        {
            JournalLine.Debit(1, null, Money.Of(100, "INR")),
            JournalLine.Credit(2, null, Money.Of(99.99m, "INR"))
        });
        Assert.True(result.IsFailure);
        Assert.Equal("UNBALANCED_ENTRY", result.Error.Code);
    }

    [Fact]
    public void Single_line_entry_is_rejected()
    {
        var result = JournalEntry.Post(Guid.NewGuid(), Date, 7, EntryType.Normal, Now, null, new[]
        {
            JournalLine.Debit(1, null, Money.Of(100, "INR"))
        });
        Assert.Equal("ENTRY_TOO_FEW_LINES", result.Error.Code);
    }

    [Fact]
    public void Each_currency_must_balance_on_its_own()
    {
        // USD leg and INR leg each balance in their own currency; base amounts also balance.
        var result = JournalEntry.Post(Guid.NewGuid(), Date, 7, EntryType.Normal, Now, null, new[]
        {
            JournalLine.Debit(1, null, Money.Of(100, "USD"), Money.Of(8300, "INR")),
            JournalLine.Credit(9, null, Money.Of(100, "USD"), Money.Of(8300, "INR")),
            JournalLine.Debit(9, null, Money.Of(8300, "INR")),
            JournalLine.Credit(2, null, Money.Of(8300, "INR"))
        });
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Line_amounts_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JournalLine.Debit(1, null, Money.Of(0, "INR")));
    }
}

public class AccountTests
{
    private static Account NewAccount() =>
        Account.Open(Guid.NewGuid(), "BA1000000001", Guid.NewGuid(), 2100, "INR", DateTimeOffset.UtcNow);

    [Fact]
    public void Debit_cannot_exceed_available_balance()
    {
        var a = NewAccount();
        Assert.True(a.Balance.Credit(Money.Of(100, "INR")).IsSuccess);

        var tooMuch = a.Balance.Debit(Money.Of(100.01m, "INR"));
        Assert.Equal("INSUFFICIENT_FUNDS", tooMuch.Error.Code);
        Assert.Equal(100m, a.Balance.LedgerBalance);

        Assert.True(a.Balance.Debit(Money.Of(100, "INR")).IsSuccess); // exactly to zero is allowed
        Assert.Equal(0m, a.Balance.LedgerBalance);
    }

    [Fact]
    public void Wrong_currency_is_rejected()
    {
        var a = NewAccount();
        Assert.Equal("CURRENCY_MISMATCH", a.Balance.Credit(Money.Of(1, "USD")).Error.Code);
    }

    [Fact]
    public void Frozen_account_can_receive_but_not_pay()
    {
        var a = NewAccount();
        Assert.True(a.Freeze().IsSuccess);
        Assert.True(a.CanCredit().IsSuccess);
        Assert.Equal("ACCOUNT_FROZEN", a.CanDebit().Error.Code);
    }

    [Fact]
    public void Only_empty_accounts_can_close_and_closed_accounts_do_nothing()
    {
        var a = NewAccount();
        a.Balance.Credit(Money.Of(5, "INR"));
        Assert.Equal("ACCOUNT_NOT_EMPTY", a.Close().Error.Code);

        a.Balance.Debit(Money.Of(5, "INR"));
        Assert.True(a.Close().IsSuccess);
        Assert.Equal("ACCOUNT_CLOSED", a.CanCredit().Error.Code);
        Assert.Equal("ACCOUNT_CLOSED", a.CanDebit().Error.Code);
    }
}
