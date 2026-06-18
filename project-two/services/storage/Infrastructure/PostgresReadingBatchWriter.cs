using System.Collections.Concurrent;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StorageService.Models;

namespace StorageService.Infrastructure;

public sealed class PostgresReadingBatchWriter(
    NpgsqlDataSource dataSource,
    StorageOptions options,
    ILogger<PostgresReadingBatchWriter> logger)
{
    private readonly ConcurrentDictionary<string, long> _deviceIds = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _deviceLock = new(1, 1);
    private bool _devicesLoaded;

    public async Task<int> PersistAsync(IReadOnlyList<QueuedReading> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return 0;
        }

        if (!options.EnableWrites)
        {
            logger.LogInformation("Database writes are disabled; dropped {Count} queued reading(s).", batch.Count);
            return batch.Count;
        }

        await EnsureDeviceCacheAsync(cancellationToken).ConfigureAwait(false);
        await EnsureKnownDevicesAsync(batch, cancellationToken).ConfigureAwait(false);

        var payloads = batch.Select(item => new
        {
            ingest_ref = item.IngestRef,
            device_id = GetDeviceId(item.DeviceExternalId),
            recorded_at = item.RecordedAt,
            readings = item.Readings
        }).ToArray();

        const string sql = """
            WITH input AS (
                SELECT *
                FROM jsonb_to_recordset(@payloads::jsonb) AS item(
                    ingest_ref text,
                    device_id bigint,
                    recorded_at timestamptz,
                    readings jsonb
                )
            ),
            inserted_readings AS (
                INSERT INTO readings (device_id, recorded_at, source_date, source_time, notes)
                SELECT
                    device_id,
                    recorded_at,
                    (recorded_at AT TIME ZONE 'UTC')::date,
                    (recorded_at AT TIME ZONE 'UTC')::time,
                    ingest_ref
                FROM input
                RETURNING id, device_id, recorded_at, notes
            )
            INSERT INTO reading_values (reading_id, sensor_type_id, numeric_value)
            SELECT
                r.id,
                st.id,
                NULLIF(v.value, '')::numeric
            FROM inserted_readings r
            JOIN input i
                ON i.device_id = r.device_id
               AND i.recorded_at = r.recorded_at
               AND i.ingest_ref = r.notes
            CROSS JOIN LATERAL jsonb_each_text(i.readings) AS v(key, value)
            JOIN sensor_types st ON st.code = v.key
            WHERE v.value IS NOT NULL
              AND v.value <> ''
              AND v.value <> '-200';
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("payloads", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(payloads)
        });

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDeviceCacheAsync(CancellationToken cancellationToken)
    {
        if (_devicesLoaded)
        {
            return;
        }

        await _deviceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_devicesLoaded)
            {
                return;
            }

            const string sql = """
                SELECT id, external_id
                FROM devices;
                """;

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var externalId = reader.GetString(1);
                _deviceIds[externalId] = id;
            }

            _devicesLoaded = true;
        }
        finally
        {
            _deviceLock.Release();
        }
    }

    private async Task EnsureKnownDevicesAsync(IReadOnlyList<QueuedReading> batch, CancellationToken cancellationToken)
    {
        var unknownExternalIds = batch
            .Select(item => item.DeviceExternalId)
            .Distinct(StringComparer.Ordinal)
            .Where(deviceId => !_deviceIds.ContainsKey(deviceId))
            .ToArray();

        if (unknownExternalIds.Length == 0)
        {
            return;
        }

        await _deviceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            unknownExternalIds = batch
                .Select(item => item.DeviceExternalId)
                .Distinct(StringComparer.Ordinal)
                .Where(deviceId => !_deviceIds.ContainsKey(deviceId))
                .ToArray();

            if (unknownExternalIds.Length == 0)
            {
                return;
            }

            var names = unknownExternalIds.ToArray();
            var locations = new string?[unknownExternalIds.Length];

            const string sql = """
                WITH input AS (
                    SELECT *
                    FROM unnest(@external_ids::text[], @names::text[], @locations::text[]) AS u(external_id, name, location)
                )
                INSERT INTO devices (external_id, name, location)
                SELECT external_id, name, location
                FROM input
                ON CONFLICT (external_id)
                DO UPDATE SET name = EXCLUDED.name
                RETURNING id, external_id;
                """;

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter("external_ids", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = unknownExternalIds
            });
            command.Parameters.Add(new NpgsqlParameter("names", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = names
            });
            command.Parameters.Add(new NpgsqlParameter("locations", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = locations
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var externalId = reader.GetString(1);
                _deviceIds[externalId] = id;
            }
        }
        finally
        {
            _deviceLock.Release();
        }
    }

    public long GetDeviceId(string deviceExternalId)
    {
        if (_deviceIds.TryGetValue(deviceExternalId, out var deviceId))
        {
            return deviceId;
        }

        throw new InvalidOperationException($"Device '{deviceExternalId}' is not known in the storage cache.");
    }
}

