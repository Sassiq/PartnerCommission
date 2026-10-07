using System.Diagnostics.Metrics;

namespace Wallet.Api.Payouts;

internal static class PayoutMetrics
{
    public const string MeterName = "PartnerCommission.Wallet";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Payouts = Meter.CreateCounter<long>("wallet.payouts", description: "Payouts made");
    private static readonly Counter<long> CommissionsPaid = Meter.CreateCounter<long>("wallet.commissions_paid", description: "Commissions paid into wallets");
    private static readonly Counter<decimal> AmountPaid = Meter.CreateCounter<decimal>("wallet.paid_amount", description: "Total amount paid into wallets");

    public static void PaidOut(int commissions, decimal amount)
    {
        Payouts.Add(1);
        CommissionsPaid.Add(commissions);
        AmountPaid.Add(amount);
    }
}
