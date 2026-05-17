namespace RestService.Domain.Entities;

public sealed class ReadingValue
{
    public long ReadingId { get; set; }
    public long SensorTypeId { get; set; }
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }

    public Reading? Reading { get; set; }
    public SensorType? SensorType { get; set; }
}
