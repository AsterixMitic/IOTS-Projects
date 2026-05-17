namespace RestService.Infrastructure.Persistence;

public sealed class ReadingAggregateRow
{
    public DateTime BucketStart { get; init; }
    public decimal AvgValue { get; init; }
    public decimal MinValue { get; init; }
    public decimal MaxValue { get; init; }
    public long Samples { get; init; }
}
