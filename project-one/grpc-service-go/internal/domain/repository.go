package domain

import "context"

type ReadingsRepository interface {
	ListSensorTypes(ctx context.Context) ([]SensorType, error)
	ListReadings(ctx context.Context, filter ReadingsFilter) ([]ReadingSummary, error)
	GetReadingByID(ctx context.Context, id int64) (*ReadingDetail, error)
	CreateReading(ctx context.Context, input CreateReadingInput) (int64, error)
	AggregateReadings(ctx context.Context, filter AggregateFilter) ([]AggregatePoint, error)
}
