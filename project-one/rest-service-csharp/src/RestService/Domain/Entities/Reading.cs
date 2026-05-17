namespace RestService.Domain.Entities;

public sealed class Reading
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateOnly? SourceDate { get; set; }
    public TimeSpan? SourceTime { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Device? Device { get; set; }
    public ICollection<ReadingValue> Values { get; set; } = new List<ReadingValue>();
}
