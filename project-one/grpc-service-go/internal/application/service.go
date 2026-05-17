package application

import (
	"context"
	"fmt"
	"slices"
	"strings"
	"time"

	"grpc-service-go/internal/domain"
)

type ReadingsService struct {
	repository domain.ReadingsRepository
}

func NewReadingsService(repository domain.ReadingsRepository) *ReadingsService {
	return &ReadingsService{repository: repository}
}

func (service *ReadingsService) ListSensorTypes(ctx context.Context) ([]domain.SensorType, error) {
	return service.repository.ListSensorTypes(ctx)
}

func (service *ReadingsService) ListReadings(ctx context.Context, filter domain.ReadingsFilter) ([]domain.ReadingSummary, error) {
	if filter.Limit <= 0 {
		filter.Limit = 100
	}

	if filter.Limit > 1000 {
		return nil, fmt.Errorf("%w: limit must be between 1 and 1000", domain.ErrInvalidArgument)
	}

	if filter.Offset < 0 {
		return nil, fmt.Errorf("%w: offset must be 0 or greater", domain.ErrInvalidArgument)
	}

	if filter.From != nil && filter.To != nil && filter.From.After(*filter.To) {
		return nil, fmt.Errorf("%w: from must be less than or equal to to", domain.ErrInvalidArgument)
	}

	filter.SensorCodes = normalizeSensorCodes(filter.SensorCodes)
	return service.repository.ListReadings(ctx, filter)
}

func (service *ReadingsService) GetReadingByID(ctx context.Context, id int64) (*domain.ReadingDetail, error) {
	if id <= 0 {
		return nil, fmt.Errorf("%w: id must be positive", domain.ErrInvalidArgument)
	}

	reading, err := service.repository.GetReadingByID(ctx, id)
	if err != nil {
		return nil, err
	}

	if reading == nil {
		return nil, domain.ErrNotFound
	}

	return reading, nil
}

func (service *ReadingsService) CreateReading(ctx context.Context, input domain.CreateReadingInput) (int64, error) {
	if input.DeviceID <= 0 {
		return 0, fmt.Errorf("%w: device_id must be positive", domain.ErrInvalidArgument)
	}

	if input.RecordedAt.IsZero() {
		input.RecordedAt = time.Now().UTC()
	}

	normalizedValues := make(map[string]float64, len(input.Values))
	for key, value := range input.Values {
		code := strings.ToLower(strings.TrimSpace(key))
		if code == "" {
			return 0, fmt.Errorf("%w: values contains empty sensor code", domain.ErrInvalidArgument)
		}

		if _, exists := normalizedValues[code]; exists {
			return 0, fmt.Errorf("%w: duplicate sensor code after normalization: %s", domain.ErrInvalidArgument, code)
		}

		normalizedValues[code] = value
	}

	if len(normalizedValues) == 0 {
		return 0, fmt.Errorf("%w: values must contain at least one sensor reading", domain.ErrInvalidArgument)
	}

	input.Values = normalizedValues
	input.RecordedAt = input.RecordedAt.UTC()
	return service.repository.CreateReading(ctx, input)
}

func (service *ReadingsService) AggregateReadings(ctx context.Context, filter domain.AggregateFilter) ([]domain.AggregatePoint, error) {
	filter.SensorCode = strings.ToLower(strings.TrimSpace(filter.SensorCode))
	if filter.SensorCode == "" {
		return nil, fmt.Errorf("%w: sensor_code is required", domain.ErrInvalidArgument)
	}

	if filter.BucketMinutes <= 0 || filter.BucketMinutes > 1440 {
		return nil, fmt.Errorf("%w: bucket_minutes must be between 1 and 1440", domain.ErrInvalidArgument)
	}

	if filter.From != nil && filter.To != nil && filter.From.After(*filter.To) {
		return nil, fmt.Errorf("%w: from must be less than or equal to to", domain.ErrInvalidArgument)
	}

	return service.repository.AggregateReadings(ctx, filter)
}

func normalizeSensorCodes(sensorCodes []string) []string {
	if len(sensorCodes) == 0 {
		return nil
	}

	normalized := make([]string, 0, len(sensorCodes))
	for _, code := range sensorCodes {
		trimmed := strings.ToLower(strings.TrimSpace(code))
		if trimmed != "" && !slices.Contains(normalized, trimmed) {
			normalized = append(normalized, trimmed)
		}
	}

	if len(normalized) == 0 {
		return nil
	}

	return normalized
}
