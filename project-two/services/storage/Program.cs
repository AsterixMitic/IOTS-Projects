using Npgsql;
using StorageService.Infrastructure;
using StorageService.Models;
using StorageService.Services;

var builder = WebApplication.CreateBuilder(args);

var storageOptions = StorageOptions.FromConfiguration(builder.Configuration);
storageOptions.Validate();

builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(storageOptions.ConnectionString));
builder.Services.AddSingleton<ReadingPayloadParser>();
builder.Services.AddSingleton<StorageMetrics>();
builder.Services.AddSingleton<PostgresReadingBatchWriter>();
builder.Services.AddSingleton<IStorageWorker>(sp =>
{
    var options = sp.GetRequiredService<StorageOptions>();

    return options.Mode switch
    {
        BrokerMode.Mqtt => ActivatorUtilities.CreateInstance<MqttStorageWorker>(sp),
        BrokerMode.Kafka => ActivatorUtilities.CreateInstance<KafkaStorageWorker>(sp),
        _ => throw new InvalidOperationException($"Unsupported broker mode '{options.Mode}'.")
    };
});
builder.Services.AddHostedService<StorageHostedService>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", (StorageOptions options) => Results.Ok(new
{
    service = "storage",
    status = "running",
    brokerMode = options.Mode.ToString().ToLowerInvariant(),
    batchSize = options.BatchSize,
    flushIntervalMs = options.FlushIntervalMilliseconds,
    writesEnabled = options.EnableWrites
}));

app.MapGet("/health", (StorageMetrics metrics) => Results.Ok(new
{
    status = "ok",
    metrics = metrics.Snapshot()
}));

app.MapGet("/config", (StorageOptions options) => Results.Ok(new
{
    brokerMode = options.Mode.ToString().ToLowerInvariant(),
    brokerUrl = options.BrokerUri.ToString(),
    mqttTopic = options.MqttTopic,
    kafkaTopic = options.KafkaTopic,
    batchSize = options.BatchSize,
    flushIntervalMs = options.FlushIntervalMilliseconds,
    writesEnabled = options.EnableWrites,
    kafkaGroupId = options.KafkaGroupId,
    kafkaClientId = options.KafkaClientId
}));

app.Run();
