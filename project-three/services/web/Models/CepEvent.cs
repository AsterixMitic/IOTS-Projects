namespace WebDashboard.Models;

// CEP događaj sa iot/events (eKuiper). Polja se razlikuju po tipu pravila — sva su opciona.
public sealed class CepEvent
{
    public string? Type { get; set; }
    public string? DeviceId { get; set; }
    public double? Value { get; set; }
    public double? Nox { get; set; }
    public double? No2 { get; set; }
    public double? Samples { get; set; }
    public string? Severity { get; set; }
    public string? Rule { get; set; }
    public long Ts { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
