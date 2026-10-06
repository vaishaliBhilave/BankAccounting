using BankAccounting.BuildingBlocks;

namespace BankAccounting.Accounts.Domain;

public static class AccountErrors
{
    public static readonly Error Frozen = Error.Unprocessable("ACCOUNT_FROZEN", "The account is frozen and cannot be debited.");
    public static readonly Error Closed = Error.Unprocessable("ACCOUNT_CLOSED", "The account is closed.");
    public static readonly Error InsufficientFunds = Error.Unprocessable("INSUFFICIENT_FUNDS", "The available balance is insufficient.");
    public static readonly Error CurrencyMismatch = Error.Validation("CURRENCY_MISMATCH", "Amount currency does not match the account currency.");
    public static readonly Error InvalidAmount = Error.Validation("INVALID_AMOUNT", "Amount must be greater than zero.");
    public static readonly Error NotFound = Error.NotFound("ACCOUNT_NOT_FOUND", "Account not found.");
    public static readonly Error NotEmpty = Error.Unprocessable("ACCOUNT_NOT_EMPTY", "Only accounts with a zero balance and no holds can be closed.");
    public static readonly Error InvalidState = Error.Unprocessable("ACCOUNT_INVALID_STATE", "The account is not in a state that allows this change.");
}
