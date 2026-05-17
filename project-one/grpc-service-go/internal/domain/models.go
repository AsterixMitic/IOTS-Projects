package domain

import "time"

type SensorType struct {
	ID    int64
	Code  string
	Label string
	Unit  string
}

type ReadingSummary struct {
	ID         int64
	DeviceID   int64
	RecordedAt time.Time
	Values     map[string]float64
}

type ReadingDetail struct {
	ID         int64
	DeviceID   int64
	RecordedAt time.Time
	SourceDate *time.Time
	SourceTime *time.Time
	Notes      string
	Values     map[string]float64
}

type AggregatePoint struct {
	BucketStart time.Time
	AvgValue    float64
	MinValue    float64
	MaxValue    float64
	Samples     int64
}

type ReadingsFilter struct {
	DeviceID    *int64
	From        *time.Time
	To          *time.Time
	SensorCodes []string
	Limit       int32
	Offset      int32
}

type AggregateFilter struct {
	SensorCode    string
	BucketMinutes int32
	DeviceID      *int64
	From          *time.Time
	To            *time.Time
}

type CreateReadingInput struct {
	DeviceID   int64
	RecordedAt time.Time
	Notes      string
	Values     map[string]float64
}
