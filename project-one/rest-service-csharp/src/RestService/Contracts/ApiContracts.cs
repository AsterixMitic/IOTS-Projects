namespace RestService.Contracts;

public sealed record SensorTypeDto(long Id, string Code, string Label, string Unit);

public sealed record ReadingSummaryDto(
    long Id,
    long DeviceId,
    DateTimeOffset RecordedAt,
    IDictionary<string, decimal?> Values);

public sealed record ReadingDetailDto(
    long Id,
    long DeviceId,
    DateTimeOffset RecordedAt,
    DateOnly? SourceDate,
    TimeSpan? SourceTime,
    string? Notes,
    IDictionary<string, decimal?> Values);

public sealed record CreateReadingRequest(
    long DeviceId,
    DateTimeOffset RecordedAt,
    IDictionary<string, decimal?> Values,
    string? Notes);

public sealed record CreateReadingResponse(long Id);

public sealed record ReadingsQuery(
    long? DeviceId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<string>? SensorCodes,
    int Limit,
    int Offset);

public sealed record ReadingAggregatePointDto(
    DateTimeOffset BucketStart,
    decimal AvgValue,
    decimal MinValue,
    decimal MaxValue,
    long Samples);
