namespace Wallet.Domain.Entities;

/// <summary>
/// Sum of paid user commissions.
/// </summary>
public sealed class UserWallet
{
    public string UserExternalId { get; set; } = "";
    public decimal Balance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
