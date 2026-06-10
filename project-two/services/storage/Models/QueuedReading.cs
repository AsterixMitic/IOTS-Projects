using System.Text.Json;

namespace StorageService.Models;

public sealed record QueuedReading(
    string DeviceExternalId,
    long DeviceId,
    DateTimeOffset RecordedAt,
    JsonElement Readings,
    string IngestRef);

