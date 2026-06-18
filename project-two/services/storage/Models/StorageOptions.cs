using System.ComponentModel.DataAnnotations;

namespace StorageService.Models;

public sealed record StorageOptions
{
    public BrokerMode Mode { get; init; } = BrokerMode.Mqtt;

    public string BrokerUrl { get; init; } = string.Empty;

    public string MqttTopic { get; init; } = "iot/readings";

    public string KafkaTopic { get; init; } = "iot.readings";

    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 500;

    [Range(100, 60_000)]
    public int FlushIntervalMilliseconds { get; init; } = 1_000;

    public bool EnableWrites { get; init; } = true;

    public string? MqttUsername { get; init; }

    public string? MqttPassword { get; init; }

    public string KafkaGroupId { get; init; } = "project-two-storage";

    public string KafkaClientId { get; init; } = "project-two-storage";

    public string ConnectionString { get; init; } = string.Empty;

    public Uri BrokerUri => ParseBrokerUri();

    public TimeSpan FlushInterval => TimeSpan.FromMilliseconds(FlushIntervalMilliseconds);

    public static StorageOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var brokerMode = ParseBrokerMode(configuration["BROKER_MODE"]);
        var brokerUrl = configuration["BROKER_URL"] ?? string.Empty;
        var connectionString = ResolveConnectionString(configuration);

        return new StorageOptions
        {
            Mode = brokerMode,
            BrokerUrl = brokerUrl,
            MqttTopic = configuration["MQTT_TOPIC"] ?? "iot/readings",
            KafkaTopic = configuration["KAFKA_TOPIC"] ?? "iot.readings",
            BatchSize = ParseInt(configuration["STORAGE_BATCH_SIZE"] ?? configuration["BATCH_SIZE"], 500),
            FlushIntervalMilliseconds = ParseInt(configuration["STORAGE_FLUSH_INTERVAL_MS"] ?? configuration["FLUSH_INTERVAL_MS"], 1_000),
            EnableWrites = ParseBool(configuration["STORAGE_ENABLE_WRITES"] ?? configuration["ENABLE_WRITES"], true),
            MqttUsername = configuration["MQTT_USERNAME"],
            MqttPassword = configuration["MQTT_PASSWORD"],
            KafkaGroupId = configuration["KAFKA_GROUP_ID"] ?? "project-two-storage",
            KafkaClientId = configuration["KAFKA_CLIENT_ID"] ?? "project-two-storage",
            ConnectionString = connectionString,
        };
    }

    public void Validate()
    {
        if (BatchSize <= 0)
        {
            throw new InvalidOperationException("STORAGE_BATCH_SIZE must be greater than zero.");
        }

        if (FlushIntervalMilliseconds <= 0)
        {
            throw new InvalidOperationException("STORAGE_FLUSH_INTERVAL_MS must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("A PostgreSQL connection string must be configured.");
        }
    }

    private Uri ParseBrokerUri()
    {
        if (!string.IsNullOrWhiteSpace(BrokerUrl) && Uri.TryCreate(BrokerUrl, UriKind.Absolute, out var brokerUri))
        {
            return brokerUri;
        }

        return Mode == BrokerMode.Kafka
            ? new Uri("kafka://kafka:9092")
            : new Uri("mqtt://mosquitto:1883");
    }

    private static BrokerMode ParseBrokerMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BrokerMode.Mqtt;
        }

        return Enum.TryParse<BrokerMode>(value, true, out var mode)
            ? mode
            : throw new InvalidOperationException($"Unsupported BROKER_MODE value '{value}'. Expected 'mqtt' or 'kafka'.");
    }

    private static int ParseInt(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        connectionString = configuration["DATABASE_URL"];

        return string.IsNullOrWhiteSpace(connectionString)
            ? string.Empty
            : connectionString;
    }
}

