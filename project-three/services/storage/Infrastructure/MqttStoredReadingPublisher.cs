using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using StorageService.Models;
using StorageService.Services;

namespace StorageService.Infrastructure;

/// <summary>
/// Re-publikuje perzistirana očitavanja na MQTT topic (podrazumevano iot/stored) posle
/// uspešnog upisa u bazu. Analytics i eKuiper se pretplaćuju na ovaj topic (Projekat 3).
/// Radi po principu best-effort — greška u publish-u ne ruši upis u bazu.
/// </summary>
public sealed class MqttStoredReadingPublisher(
    StorageOptions options,
    StorageMetrics metrics,
    ILogger<MqttStoredReadingPublisher> logger) : IStoredReadingPublisher, IAsyncDisposable
{
    private readonly IMqttClient _client = new MqttFactory().CreateMqttClient();
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public async Task PublishAsync(IReadOnlyList<QueuedReading> batch, CancellationToken cancellationToken)
    {
        if (!options.Republish || batch.Count == 0)
        {
            return;
        }

        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            foreach (var reading in batch)
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(options.MqttStoredTopic)
                    .WithPayload(BuildPayload(reading))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                metrics.RecordRepublished();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to republish batch to {Topic}.", options.MqttStoredTopic);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            var builder = new MqttClientOptionsBuilder()
                .WithClientId($"storage-pub-{Environment.ProcessId}")
                .WithTcpServer(options.BrokerUri.Host, options.BrokerUri.Port > 0 ? options.BrokerUri.Port : 1883)
                .WithProtocolVersion(MqttProtocolVersion.V500);

            if (!string.IsNullOrWhiteSpace(options.MqttUsername))
            {
                builder.WithCredentials(options.MqttUsername, options.MqttPassword);
            }

            await _client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "MQTT re-publisher connected to {BrokerUrl}, publishing to {Topic}.",
                options.BrokerUri, options.MqttStoredTopic);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Payload = originalno očitavanje (deviceId, timestamp, readings) + storedAt (server vreme),
    /// tako da downstream servisi mogu da mere end-to-end latenciju.
    /// </summary>
    private static byte[] BuildPayload(QueuedReading reading)
    {
        var envelope = new
        {
            deviceId = reading.DeviceExternalId,
            timestamp = reading.RecordedAt.ToUnixTimeMilliseconds(),
            readings = reading.Readings,
            storedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }

        _client.Dispose();
        _connectLock.Dispose();
    }
}
