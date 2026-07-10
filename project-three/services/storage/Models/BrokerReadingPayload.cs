using System.Text.Json;
using System.Text.Json.Serialization;

namespace StorageService.Models;

public sealed record BrokerReadingPayload(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("readings")] JsonElement Readings)
{
    public DateTimeOffset RecordedAt => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);
}

