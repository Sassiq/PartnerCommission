using System.Diagnostics.Metrics;
using PartnerCommission.Contracts;

namespace Commission.Api.Messaging;

internal static class CommissionMetrics
{
    public const string MeterName = "PartnerCommission.Commission";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> EventsProcessed =
        Meter.CreateCounter<long>("commission.events_processed", description: "Profit events stored and calculated");

    private static readonly Counter<long> CommissionsAccrued =
        Meter.CreateCounter<long>("commission.accrued", description: "Commissions accrued");

    private static readonly Counter<decimal> AmountAccrued =
        Meter.CreateCounter<decimal>("commission.accrued_amount", description: "Total amount of accrued commissions");

    public static void EventProcessed() => EventsProcessed.Add(1);

    public static void Accrued(SchemaType schema, decimal amount)
    {
        var tag = new KeyValuePair<string, object?>("schema_type", schema.ToString());
        CommissionsAccrued.Add(1, tag);
        AmountAccrued.Add(amount, tag);
    }
}
