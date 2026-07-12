namespace WebDashboard.Models;

// Perzistirano očitavanje sa iot/stored.
public sealed class ReadingMessage
{
    public string? DeviceId { get; set; }
    public long Timestamp { get; set; }
    public Dictionary<string, double>? Readings { get; set; }
    public long StoredAt { get; set; }

    public double? Temperature =>
        Readings != null && Readings.TryGetValue("temperature", out var t) ? t : null;
}
