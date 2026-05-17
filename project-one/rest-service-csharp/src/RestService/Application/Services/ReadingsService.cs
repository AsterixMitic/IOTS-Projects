using Microsoft.EntityFrameworkCore;
using RestService.Contracts;
using RestService.Domain.Entities;
using RestService.Infrastructure.Persistence;

namespace RestService.Application.Services;

public sealed class ReadingsService(IotDbContext dbContext) : IReadingsService
{
    public async Task<IReadOnlyList<SensorTypeDto>> GetSensorTypesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.SensorTypes.AsNoTracking()
            .OrderBy(sensorType => sensorType.Id)
            .Select(sensorType => new SensorTypeDto(
                sensorType.Id,
                sensorType.Code,
                sensorType.Label,
                sensorType.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReadingSummaryDto>> GetReadingsAsync(ReadingsQuery query, CancellationToken cancellationToken)
    {
        var sensorCodes = query.SensorCodes?.ToArray() ?? [];

        var readingsQuery = dbContext.Readings.AsNoTracking()
            .Where(reading => !query.DeviceId.HasValue || reading.DeviceId == query.DeviceId.Value)
            .Where(reading => !query.From.HasValue || reading.RecordedAt >= query.From.Value.UtcDateTime)
            .Where(reading => !query.To.HasValue || reading.RecordedAt <= query.To.Value.UtcDateTime);

        var readingRows = await readingsQuery
            .OrderByDescending(reading => reading.RecordedAt)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(reading => new
            {
                reading.Id,
                reading.DeviceId,
                reading.RecordedAt
            })
            .ToListAsync(cancellationToken);

        if (readingRows.Count == 0)
        {
            return [];
        }

        var readingIds = readingRows.Select(row => row.Id).ToArray();

        var valuesQuery = dbContext.ReadingValues.AsNoTracking()
            .Where(readingValue => readingIds.Contains(readingValue.ReadingId))
            .Where(readingValue => readingValue.SensorType != null);

        if (sensorCodes.Length > 0)
        {
            valuesQuery = valuesQuery.Where(readingValue => sensorCodes.Contains(readingValue.SensorType!.Code));
        }

        var valueRows = await valuesQuery
            .Select(readingValue => new
            {
                readingValue.ReadingId,
                SensorCode = readingValue.SensorType!.Code,
                readingValue.NumericValue
            })
            .ToListAsync(cancellationToken);

        var valueLookup = valueRows
            .GroupBy(row => row.ReadingId)
            .ToDictionary(
                group => group.Key,
                group => (IDictionary<string, decimal?>)group
                    .ToDictionary(entry => entry.SensorCode, entry => entry.NumericValue, StringComparer.Ordinal));

        return readingRows.Select(row => new ReadingSummaryDto(
                row.Id,
                row.DeviceId,
                ToUtcOffset(row.RecordedAt),
                valueLookup.GetValueOrDefault(row.Id, new Dictionary<string, decimal?>())))
            .ToList();
    }

    public async Task<ReadingDetailDto?> GetReadingByIdAsync(long id, CancellationToken cancellationToken)
    {
        var readingRow = await dbContext.Readings.AsNoTracking()
            .Where(reading => reading.Id == id)
            .Select(reading => new
            {
                reading.Id,
                reading.DeviceId,
                reading.RecordedAt,
                reading.SourceDate,
                reading.SourceTime,
                reading.Notes
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (readingRow is null)
        {
            return null;
        }

        var values = await dbContext.ReadingValues.AsNoTracking()
            .Where(readingValue => readingValue.ReadingId == id && readingValue.SensorType != null)
            .Select(readingValue => new
            {
                SensorCode = readingValue.SensorType!.Code,
                readingValue.NumericValue
            })
            .ToDictionaryAsync(
                row => row.SensorCode,
                row => row.NumericValue,
                StringComparer.Ordinal,
                cancellationToken);

        return new ReadingDetailDto(
            readingRow.Id,
            readingRow.DeviceId,
            ToUtcOffset(readingRow.RecordedAt),
            readingRow.SourceDate,
            readingRow.SourceTime,
            readingRow.Notes,
            values);
    }

    public async Task<long> CreateReadingAsync(CreateReadingRequest request, CancellationToken cancellationToken)
    {
        if (request.DeviceId <= 0)
        {
            throw new ArgumentException("Field 'deviceId' must be a positive value.");
        }

        if (request.Values is null || request.Values.Count == 0)
        {
            throw new ArgumentException("Field 'values' must contain at least one sensor reading.");
        }

        var normalizedEntries = request.Values
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => new
            {
                Code = entry.Key.Trim().ToLowerInvariant(),
                entry.Value
            })
            .ToList();

        var duplicateCodes = normalizedEntries
            .GroupBy(entry => entry.Code, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        if (duplicateCodes.Length > 0)
        {
            throw new ArgumentException($"Duplicate sensor code(s) after normalization: {string.Join(", ", duplicateCodes)}.");
        }

        var normalizedValues = normalizedEntries
            .Where(entry => entry.Value.HasValue)
            .Select(entry => new KeyValuePair<string, decimal?>(
                entry.Code,
                entry.Value))
            .ToList();

        if (normalizedValues.Count == 0)
        {
            throw new ArgumentException("Field 'values' must contain at least one non-null numeric value.");
        }

        var deviceExists = await dbContext.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == request.DeviceId, cancellationToken);

        if (!deviceExists)
        {
            throw new ArgumentException($"Device with id {request.DeviceId} does not exist.");
        }

        var requestedCodes = normalizedValues
            .Select(entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var sensorLookup = await dbContext.SensorTypes.AsNoTracking()
            .Where(sensorType => requestedCodes.Contains(sensorType.Code))
            .ToDictionaryAsync(sensorType => sensorType.Code, sensorType => sensorType.Id, StringComparer.Ordinal, cancellationToken);

        var missingCodes = requestedCodes.Where(code => !sensorLookup.ContainsKey(code)).ToArray();
        if (missingCodes.Length > 0)
        {
            throw new ArgumentException($"Unknown sensor code(s): {string.Join(", ", missingCodes)}.");
        }

        var recordedAtUtc = request.RecordedAt.UtcDateTime;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var reading = new Reading
        {
            DeviceId = request.DeviceId,
            RecordedAt = recordedAtUtc,
            SourceDate = DateOnly.FromDateTime(recordedAtUtc),
            SourceTime = recordedAtUtc.TimeOfDay,
            Notes = request.Notes
        };

        dbContext.Readings.Add(reading);
        await dbContext.SaveChangesAsync(cancellationToken);

        var readingValues = normalizedValues.Select(entry => new ReadingValue
        {
            ReadingId = reading.Id,
            SensorTypeId = sensorLookup[entry.Key],
            NumericValue = entry.Value
        });

        dbContext.ReadingValues.AddRange(readingValues);
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return reading.Id;
    }

    public async Task<IReadOnlyList<ReadingAggregatePointDto>> GetAggregatesAsync(
        string sensorCode,
        int bucketMinutes,
        long? deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ReadingAggregateRows
            .FromSqlInterpolated($"""
                SELECT
                    date_bin(
                        make_interval(mins => {bucketMinutes}),
                        r.recorded_at,
                        TIMESTAMPTZ '2000-01-01 00:00:00+00'
                    ) AS bucket_start,
                    AVG(rv.numeric_value) AS avg_value,
                    MIN(rv.numeric_value) AS min_value,
                    MAX(rv.numeric_value) AS max_value,
                    COUNT(*)::bigint AS samples
                FROM readings r
                JOIN reading_values rv ON rv.reading_id = r.id
                JOIN sensor_types st ON st.id = rv.sensor_type_id
                WHERE st.code = {sensorCode}
                  AND ({deviceId} IS NULL OR r.device_id = {deviceId})
                  AND ({from} IS NULL OR r.recorded_at >= {from})
                  AND ({to} IS NULL OR r.recorded_at <= {to})
                GROUP BY bucket_start
                ORDER BY bucket_start;
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ReadingAggregatePointDto(
            ToUtcOffset(row.BucketStart),
            row.AvgValue,
            row.MinValue,
            row.MaxValue,
            row.Samples)).ToList();
    }

    private static DateTimeOffset ToUtcOffset(DateTime timestamp)
    {
        var utcTimestamp = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utcTimestamp);
    }
}
