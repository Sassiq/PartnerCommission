namespace Wallet.Api.Models;

public sealed record BalanceResponse(
    string UserExternalId,
    decimal Balance,
    decimal PendingAmount);
