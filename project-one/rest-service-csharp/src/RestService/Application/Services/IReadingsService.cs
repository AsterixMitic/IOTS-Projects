using RestService.Contracts;

namespace RestService.Application.Services;

public interface IReadingsService
{
    Task<IReadOnlyList<SensorTypeDto>> GetSensorTypesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ReadingSummaryDto>> GetReadingsAsync(ReadingsQuery query, CancellationToken cancellationToken);
    Task<ReadingDetailDto?> GetReadingByIdAsync(long id, CancellationToken cancellationToken);
    Task<long> CreateReadingAsync(CreateReadingRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReadingAggregatePointDto>> GetAggregatesAsync(
        string sensorCode,
        int bucketMinutes,
        long? deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken);
}
