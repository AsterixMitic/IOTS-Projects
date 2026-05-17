using Microsoft.EntityFrameworkCore;
using RestService.Domain.Entities;

namespace RestService.Infrastructure.Persistence;

public sealed class IotDbContext(DbContextOptions<IotDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<SensorType> SensorTypes => Set<SensorType>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<ReadingValue> ReadingValues => Set<ReadingValue>();
    public DbSet<ReadingAggregateRow> ReadingAggregateRows => Set<ReadingAggregateRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(device => device.Id);
            entity.Property(device => device.Id).HasColumnName("id");
            entity.Property(device => device.ExternalId).HasColumnName("external_id");
            entity.Property(device => device.Name).HasColumnName("name");
            entity.Property(device => device.Location).HasColumnName("location");
            entity.Property(device => device.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SensorType>(entity =>
        {
            entity.ToTable("sensor_types");
            entity.HasKey(sensorType => sensorType.Id);
            entity.Property(sensorType => sensorType.Id).HasColumnName("id");
            entity.Property(sensorType => sensorType.Code).HasColumnName("code");
            entity.Property(sensorType => sensorType.Label).HasColumnName("label");
            entity.Property(sensorType => sensorType.Unit).HasColumnName("unit");
        });

        modelBuilder.Entity<Reading>(entity =>
        {
            entity.ToTable("readings");
            entity.HasKey(reading => reading.Id);
            entity.Property(reading => reading.Id).HasColumnName("id");
            entity.Property(reading => reading.DeviceId).HasColumnName("device_id");
            entity.Property(reading => reading.RecordedAt).HasColumnName("recorded_at");
            entity.Property(reading => reading.SourceDate).HasColumnName("source_date");
            entity.Property(reading => reading.SourceTime).HasColumnName("source_time");
            entity.Property(reading => reading.Notes).HasColumnName("notes");
            entity.Property(reading => reading.CreatedAt).HasColumnName("created_at");
            entity.HasOne(reading => reading.Device)
                .WithMany(device => device.Readings)
                .HasForeignKey(reading => reading.DeviceId);
        });

        modelBuilder.Entity<ReadingValue>(entity =>
        {
            entity.ToTable("reading_values");
            entity.HasKey(readingValue => new { readingValue.ReadingId, readingValue.SensorTypeId });
            entity.Property(readingValue => readingValue.ReadingId).HasColumnName("reading_id");
            entity.Property(readingValue => readingValue.SensorTypeId).HasColumnName("sensor_type_id");
            entity.Property(readingValue => readingValue.NumericValue).HasColumnName("numeric_value");
            entity.Property(readingValue => readingValue.TextValue).HasColumnName("text_value");
            entity.HasOne(readingValue => readingValue.Reading)
                .WithMany(reading => reading.Values)
                .HasForeignKey(readingValue => readingValue.ReadingId);
            entity.HasOne(readingValue => readingValue.SensorType)
                .WithMany(sensorType => sensorType.ReadingValues)
                .HasForeignKey(readingValue => readingValue.SensorTypeId);
        });

        modelBuilder.Entity<ReadingAggregateRow>(entity =>
        {
            entity.HasNoKey();
            entity.ToView(null);
            entity.Property(row => row.BucketStart).HasColumnName("bucket_start");
            entity.Property(row => row.AvgValue).HasColumnName("avg_value");
            entity.Property(row => row.MinValue).HasColumnName("min_value");
            entity.Property(row => row.MaxValue).HasColumnName("max_value");
            entity.Property(row => row.Samples).HasColumnName("samples");
        });
    }
}
