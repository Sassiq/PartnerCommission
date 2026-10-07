using System.Text.Json;
using PartnerCommission.Contracts;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging;
using Shouldly;

namespace Messaging.UnitTests;

public class MessageSerializerTests
{
    [Fact]
    public void CommissionAccrued_survives_a_round_trip()
    {
        var original = new CommissionAccrued(
            Guid.NewGuid(), "evt-1", "partner-7", Level: 3, Amount: 12.34567891m, SchemaType.Fibonacci,
            new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

        var copy = MessageSerializer.Deserialize<CommissionAccrued>(MessageSerializer.Serialize(original));

        copy.ShouldBe(original);
    }

    [Fact]
    public void Money_keeps_all_eight_decimals()
    {
        var json = MessageSerializer.Serialize(new ProfitEventReceived("e", "u", 0.00000001m, DateTimeOffset.UnixEpoch));

        MessageSerializer.Deserialize<ProfitEventReceived>(json).Profit.ShouldBe(0.00000001m);
    }

    [Fact]
    public void Enums_and_names_use_readable_json()
    {
        var json = MessageSerializer.Serialize(
            new CommissionAccrued(Guid.Empty, "e", "b", 1, 1m, SchemaType.Linear, DateTimeOffset.UnixEpoch));

        json.ShouldContain("\"schemaType\":\"Linear\"");
        json.ShouldContain("\"partnerExternalId\":\"b\"");
    }

    [Fact]
    public void CommissionsPaid_keeps_the_id_list()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var original = new CommissionsPaid(Guid.NewGuid(), "u", ids, 5m, DateTimeOffset.UnixEpoch);

        var copy = MessageSerializer.Deserialize<CommissionsPaid>(MessageSerializer.Serialize(original));

        copy.CommissionIds.ShouldBe(ids);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"externalId\": 5}")]
    public void Invalid_payload_throws_JsonException(string payload) =>
        Should.Throw<JsonException>(() => MessageSerializer.Deserialize<ProfitEventReceived>(payload));
}
