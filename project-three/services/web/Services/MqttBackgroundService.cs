using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using WebDashboard.Models;

namespace WebDashboard.Services;

// Server-side MQTT pretplata (MQTTnet). Prima iot/analytics, iot/events, iot/stored,
// puni DashboardState. Blazor Server prosleđuje promene u UI preko SignalR-a.
public sealed class MqttBackgroundService(
    DashboardState state,
    IConfiguration config,
    ILogger<MqttBackgroundService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions CamelJson = new() { PropertyNameCaseInsensitive = true };

    private int _readingsDirty;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host           = config["MQTT_HOST"] ?? "mosquitto";
        var port           = int.TryParse(config["MQTT_PORT"], out var p) ? p : 1883;
        var storedTopic    = config["MQTT_STORED_TOPIC"]    ?? "iot/stored";
        var eventsTopic    = config["MQTT_EVENTS_TOPIC"]    ?? "iot/events";
        var analyticsTopic = config["MQTT_ANALYTICS_TOPIC"] ?? "iot/analytics";

        using var client = new MqttFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId($"web-dashboard-{Environment.ProcessId}")
            .WithTcpServer(host, port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .Build();

        client.ApplicationMessageReceivedAsync += args =>
        {
            var topic = args.ApplicationMessage.Topic;
            var payload = args.ApplicationMessage.ConvertPayloadToString();
            try
            {
                HandleMessage(topic, payload, storedTopic, eventsTopic, analyticsTopic);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to handle message on {Topic}", topic);
            }
            return Task.CompletedTask;
        };

        client.DisconnectedAsync += _ =>
        {
            state.SetConnected(false);
            return Task.CompletedTask;
        };

        // Tajmer koji osvežava UI za očitavanja najviše jednom u sekundi (anti-flood).
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (Interlocked.Exchange(ref _readingsDirty, 0) == 1) state.Notify();
            }
        }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(options, stoppingToken).ConfigureAwait(false);
                    await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(storedTopic,    MqttQualityOfServiceLevel.AtLeastOnce)
                        .WithTopicFilter(eventsTopic,    MqttQualityOfServiceLevel.AtLeastOnce)
                        .WithTopicFilter(analyticsTopic, MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build(), stoppingToken).ConfigureAwait(false);

                    state.SetConnected(true);
                    logger.LogInformation("Dashboard MQTT connected to {Host}:{Port} and subscribed.", host, port);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MQTT loop error; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private void HandleMessage(string topic, string payload, string stored, string events, string analytics)
    {
        if (topic == analytics)
        {
            var summary = JsonSerializer.Deserialize<AnalyticsSummary>(payload, CamelJson);
            if (summary != null) state.SetSummary(summary);
        }
        else if (topic == events)
        {
            var evt = JsonSerializer.Deserialize<CepEvent>(payload, CamelJson);
            if (evt != null) state.AddEvent(evt);
        }
        else if (topic == stored)
        {
            var reading = JsonSerializer.Deserialize<ReadingMessage>(payload, CamelJson);
            if (reading != null)
            {
                state.AddReading(reading);
                Interlocked.Exchange(ref _readingsDirty, 1);
            }
        }
    }
}
