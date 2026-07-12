namespace WebDashboard.Models;

// Rezime koji Analytics publikuje na iot/analytics (camelCase JSON iz Node servisa).
public sealed class AnalyticsSummary
{
    public string? Type { get; set; }
    public int Window { get; set; }
    public int Count { get; set; }
    public double? AvgTemp { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? AvgLatencyMs { get; set; }
    public bool TempAlert { get; set; }
    public string? AirQuality { get; set; }
    public Dictionary<string, double>? AirQualityProb { get; set; }
    public long Ts { get; set; }
}
