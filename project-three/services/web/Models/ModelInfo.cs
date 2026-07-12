namespace WebDashboard.Models;

// Odgovor MaaS /model/info (snake_case JSON iz Python servisa).
public sealed class ModelInfo
{
    public string? ModelVersion { get; set; }
    public string? ModelType { get; set; }
    public string? Library { get; set; }
    public string? Task { get; set; }
    public List<string>? Classes { get; set; }
    public ModelMetrics? Metrics { get; set; }
}

public sealed class ModelMetrics
{
    public double ValAccuracy { get; set; }
    public double TestAccuracy { get; set; }
}
