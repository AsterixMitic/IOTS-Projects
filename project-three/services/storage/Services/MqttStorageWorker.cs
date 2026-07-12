using System.Diagnostics;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using StorageService.Infrastructure;
using StorageService.Models;

namespace StorageService.Services;

public sealed class MqttStorageWorker(
    StorageOptions options,
    ReadingPayloadParser parser,
    PostgresReadingBatchWriter writer,
    IStoredReadingPublisher publisher,
    StorageMetrics metrics,
    ILogger<MqttStorageWorker> logger) : IStorageWorker
{
    private readonly Channel<QueuedReading> _channel = Channel.CreateBounded<QueuedReading>(new BoundedChannelOptions(Math.Max(options.BatchSize * 10, 1_000))
    {
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var processingTask = ProcessBatchesAsync(cancellationToken);
        var consumeTask = ConsumeAsync(cancellationToken);

        await Task.WhenAll(processingTask, consumeTask).ConfigureAwait(false);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        var clientOptions = CreateClientOptions();
        client.ApplicationMessageReceivedAsync += async args =>
        {
            try
            {
                var payload = args.ApplicationMessage.ConvertPayloadToString();
                var reading = parser.Parse(payload);
                var queuedReading = new QueuedReading(
                    reading.DeviceId,
                    0L,
                    reading.RecordedAt,
                    reading.Readings,
                    Guid.NewGuid().ToString("N"));

                await _channel.Writer.WriteAsync(queuedReading, cancellationToken).ConfigureAwait(false);
                metrics.RecordReceived();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                metrics.RecordFailed();
                logger.LogWarning(ex, "Failed to process MQTT message.");
            }
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(clientOptions, cancellationToken).ConfigureAwait(false);
                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(filter => filter
                            .WithTopic(options.MqttTopic)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                        .Build();

                    await client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("MQTT subscriber connected to {BrokerUrl} and subscribed to {Topic}.", options.BrokerUri, options.MqttTopic);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MQTT connection loop failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }

        _channel.Writer.TryComplete();
    }

    private async Task ProcessBatchesAsync(CancellationToken cancellationToken)
    {
        var buffer = new List<QueuedReading>(options.BatchSize);
        var lastFlush = Stopwatch.GetTimestamp();

        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                buffer.Add(item);

                if (buffer.Count >= options.BatchSize)
                {
                    await FlushAsync(buffer, cancellationToken).ConfigureAwait(false);
                    lastFlush = Stopwatch.GetTimestamp();
                }
            }

            if (buffer.Count == 0)
            {
                continue;
            }

            var remaining = options.FlushInterval - Stopwatch.GetElapsedTime(lastFlush);
            if (remaining <= TimeSpan.Zero)
            {
                await FlushAsync(buffer, cancellationToken).ConfigureAwait(false);
                lastFlush = Stopwatch.GetTimestamp();
                continue;
            }

            // Task.Delay is safe to recreate every iteration, unlike PeriodicTimer.WaitForNextTickAsync
            // (which throws if a previous call is abandoned mid-flight instead of awaited to completion).
            var waitTask = _channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var delayTask = Task.Delay(remaining, cancellationToken);

            var completed = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);
            if (completed == delayTask)
            {
                await FlushAsync(buffer, cancellationToken).ConfigureAwait(false);
                lastFlush = Stopwatch.GetTimestamp();
            }
        }

        if (buffer.Count > 0)
        {
            await FlushAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task FlushAsync(List<QueuedReading> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        var batch = buffer.ToArray();

        var started = Stopwatch.GetTimestamp();
        try
        {
            await writer.PersistAsync(batch, cancellationToken).ConfigureAwait(false);
            buffer.Clear();
            metrics.RecordPersisted(batch.Length, Stopwatch.GetElapsedTime(started));

            // Projekat 3: re-publikuj perzistirana očitavanja na iot/stored (best-effort).
            await publisher.PublishAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            metrics.RecordFailed();
            logger.LogError(ex, "Failed to persist MQTT batch of {Count} message(s).", batch.Length);
        }
    }

    private MqttClientOptions CreateClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId($"storage-mqtt-{Environment.ProcessId}")
            .WithTcpServer(options.BrokerUri.Host, options.BrokerUri.Port > 0 ? options.BrokerUri.Port : 1883)
            .WithProtocolVersion(MqttProtocolVersion.V500);

        if (!string.IsNullOrWhiteSpace(options.MqttUsername))
        {
            builder.WithCredentials(options.MqttUsername, options.MqttPassword);
        }

        return builder.Build();
    }
}

