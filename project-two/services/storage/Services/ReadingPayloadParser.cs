using System.Text;
using System.Text.Json;
using StorageService.Models;

namespace StorageService.Services;

public sealed class ReadingPayloadParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BrokerReadingPayload Parse(ReadOnlySpan<byte> payload)
    {
        var reading = JsonSerializer.Deserialize<BrokerReadingPayload>(payload, SerializerOptions);

        if (reading is null)
        {
            throw new InvalidDataException("Message payload is empty.");
        }

        Validate(reading);
        return reading;
    }

    public BrokerReadingPayload Parse(string payload)
        => Parse(Encoding.UTF8.GetBytes(payload));

    private static void Validate(BrokerReadingPayload reading)
    {
        if (string.IsNullOrWhiteSpace(reading.DeviceId))
        {
            throw new InvalidDataException("Message payload is missing deviceId.");
        }

        if (reading.Timestamp <= 0)
        {
            throw new InvalidDataException("Message payload is missing a valid timestamp.");
        }

        if (reading.Readings.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Message payload must include a readings object.");
        }
    }
}

