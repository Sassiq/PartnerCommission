using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartnerCommission.Messaging;

public static class MessageHeaders
{
    public const string MessageId = "message-id";
    public const string MessageType = "message-type";

    public const string DeadLetterError = "dlt-error";
    public const string DeadLetterExceptionType = "dlt-exception-type";
    public const string DeadLetterOriginalTopic = "dlt-original-topic";
    public const string DeadLetterOriginalPartition = "dlt-original-partition";
    public const string DeadLetterOriginalOffset = "dlt-original-offset";
    public const string DeadLetterAttempts = "dlt-attempts";
}

public static class MessageSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Message payload is null.");
}
