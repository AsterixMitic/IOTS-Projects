namespace RestService.Domain.Entities;

public sealed class SensorType
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public ICollection<ReadingValue> ReadingValues { get; set; } = new List<ReadingValue>();
}
