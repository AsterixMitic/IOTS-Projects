namespace RestService.Domain.Entities;

public sealed class Device
{
    public long Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Reading> Readings { get; set; } = new List<Reading>();
}
