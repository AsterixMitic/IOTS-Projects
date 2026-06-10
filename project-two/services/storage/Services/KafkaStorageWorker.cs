using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using StorageService.Infrastructure;
using StorageService.Models;

namespace StorageService.Services;

public sealed class KafkaStorageWorker(
    StorageOptions options,
    ReadingPayloadParser parser,
    PostgresReadingBatchWriter writer,
    StorageMetrics metrics,
    ILogger<KafkaStorageWorker> logger) : IStorageWorker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var config = CreateConsumerConfig();
        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe(options.KafkaTopic);

        var buffer = new List<KafkaBatchItem>(options.BatchSize);
        var lastFlush = Stopwatch.GetTimestamp();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<Ignore, string>? result = null;

                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                }
                catch (ConsumeException ex)
                {
                    metrics.RecordFailed();
                    logger.LogWarning(ex, "Kafka consumer error.");
                }

                if (result is not null)
                {
                    try
                    {
                        var payload = parser.Parse(Encoding.UTF8.GetBytes(result.Message.Value));
                        var queued = new QueuedReading(
                            payload.DeviceId,
                            0L,
                            payload.RecordedAt,
                            payload.Readings,
                            Guid.NewGuid().ToString("N"));

                        buffer.Add(new KafkaBatchItem(queued, result));
                        metrics.RecordReceived();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        metrics.RecordFailed();
                        logger.LogWarning(ex, "Failed to parse Kafka message.");
                    }
                }

                if (buffer.Count > 0 && (buffer.Count >= options.BatchSize || Stopwatch.GetElapsedTime(lastFlush) >= options.FlushInterval))
                {
                    await FlushAsync(buffer, consumer, cancellationToken).ConfigureAwait(false);
                    lastFlush = Stopwatch.GetTimestamp();
                }
            }
        }
        finally
        {
            if (buffer.Count > 0)
            {
                await FlushAsync(buffer, consumer, CancellationToken.None).ConfigureAwait(false);
            }

            consumer.Close();
        }
    }

    private async Task FlushAsync(List<KafkaBatchItem> buffer, IConsumer<Ignore, string> consumer, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        var batch = buffer.ToArray();

        var started = Stopwatch.GetTimestamp();
        try
        {
            await writer.PersistAsync(batch.Select(item => item.Reading).ToArray(), cancellationToken).ConfigureAwait(false);
            buffer.Clear();
            metrics.RecordPersisted(batch.Length, Stopwatch.GetElapsedTime(started));

            foreach (var partitionGroup in batch
                         .GroupBy(item => item.Result.TopicPartition)
                         .Select(group => group.OrderBy(item => item.Result.Offset.Value).Last())
                         .Select(item => item.Result))
            {
                consumer.Commit(partitionGroup);
            }
        }
        catch (Exception ex)
        {
            metrics.RecordFailed();
            logger.LogError(ex, "Failed to persist Kafka batch of {Count} message(s).", batch.Length);
        }
    }

    private ConsumerConfig CreateConsumerConfig()
    {
        var brokers = ParseKafkaBrokers(options.BrokerUri);

        return new ConsumerConfig
        {
            BootstrapServers = string.Join(",", brokers),
            GroupId = options.KafkaGroupId,
            ClientId = options.KafkaClientId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = true
        };
    }

    private static IReadOnlyList<string> ParseKafkaBrokers(Uri brokerUri)
    {
        if (brokerUri.Scheme.Equals("kafka", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(brokerUri.Host)
                ? ["kafka:9092"]
                : [ $"{brokerUri.Host}:{(brokerUri.Port > 0 ? brokerUri.Port : 9092)}" ];
        }

        return ["kafka:9092"];
    }

    private sealed record KafkaBatchItem(QueuedReading Reading, ConsumeResult<Ignore, string> Result);
}

