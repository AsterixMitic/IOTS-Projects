package infra

import (
	"context"
	"encoding/json"
	"fmt"
	"strings"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	"grpc-service-go/internal/domain"
)

type PgxRepository struct {
	pool *pgxpool.Pool
}

func NewPgxRepository(pool *pgxpool.Pool) *PgxRepository {
	return &PgxRepository{pool: pool}
}

func (r *PgxRepository) ListSensorTypes(ctx context.Context) ([]domain.SensorType, error) {
	const sql = `SELECT id, code, label, unit FROM sensor_types ORDER BY id`
	rows, err := r.pool.Query(ctx, sql)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	res := make([]domain.SensorType, 0)
	for rows.Next() {
		var id int64
		var code, label, unit string
		if err := rows.Scan(&id, &code, &label, &unit); err != nil {
			return nil, err
		}
		res = append(res, domain.SensorType{ID: id, Code: code, Label: label, Unit: unit})
	}
	return res, nil
}

func (r *PgxRepository) ListReadings(ctx context.Context, filter domain.ReadingsFilter) ([]domain.ReadingSummary, error) {
	args := make([]any, 0)
	where := make([]string, 0)
	idx := 1
	if filter.DeviceID != nil {
		where = append(where, fmt.Sprintf("r.device_id = $%d", idx))
		args = append(args, *filter.DeviceID)
		idx++
	}
	if filter.From != nil {
		where = append(where, fmt.Sprintf("r.recorded_at >= $%d", idx))
		args = append(args, *filter.From)
		idx++
	}
	if filter.To != nil {
		where = append(where, fmt.Sprintf("r.recorded_at <= $%d", idx))
		args = append(args, *filter.To)
		idx++
	}
	if len(filter.SensorCodes) > 0 {
		where = append(where, fmt.Sprintf("st.code = ANY($%d)", idx))
		args = append(args, filter.SensorCodes)
		idx++
	}
	if filter.Limit <= 0 {
		filter.Limit = 100
	}
	if filter.Offset < 0 {
		filter.Offset = 0
	}
	// append limit and offset
	args = append(args, int(filter.Limit))
	limitIdx := idx
	idx++
	args = append(args, int(filter.Offset))
	offsetIdx := idx

	sql := strings.Builder{}
	sql.WriteString("SELECT r.id, r.device_id, r.recorded_at, jsonb_object_agg(st.code, rv.numeric_value) AS values_json FROM readings r JOIN reading_values rv ON rv.reading_id = r.id JOIN sensor_types st ON st.id = rv.sensor_type_id")
	if len(where) > 0 {
		sql.WriteString(" WHERE ")
		sql.WriteString(strings.Join(where, " AND "))
	}
	sql.WriteString(fmt.Sprintf(" GROUP BY r.id ORDER BY r.recorded_at DESC LIMIT $%d OFFSET $%d", limitIdx, offsetIdx))

	rows, err := r.pool.Query(ctx, sql.String(), args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	res := make([]domain.ReadingSummary, 0)
	for rows.Next() {
		var id int64
		var deviceID int64
		var recordedAt time.Time
		var valuesJSON []byte
		if err := rows.Scan(&id, &deviceID, &recordedAt, &valuesJSON); err != nil {
			return nil, err
		}
		vals := map[string]float64{}
		if valuesJSON != nil {
			if err := json.Unmarshal(valuesJSON, &vals); err != nil {
				return nil, err
			}
		}
		res = append(res, domain.ReadingSummary{ID: id, DeviceID: deviceID, RecordedAt: recordedAt, Values: vals})
	}
	return res, nil
}

func (r *PgxRepository) GetReadingByID(ctx context.Context, id int64) (*domain.ReadingDetail, error) {
	const sql = `SELECT r.id, r.device_id, r.recorded_at, r.source_date, r.source_time, r.notes, jsonb_object_agg(st.code, rv.numeric_value) AS values_json FROM readings r JOIN reading_values rv ON rv.reading_id = r.id JOIN sensor_types st ON st.id = rv.sensor_type_id WHERE r.id = $1 GROUP BY r.id`
	row := r.pool.QueryRow(ctx, sql, id)
	var rid int64
	var deviceID int64
	var recordedAt time.Time
	var sourceDate *string
	var sourceTime *string
	var notes *string
	var valuesJSON []byte
	if err := row.Scan(&rid, &deviceID, &recordedAt, &sourceDate, &sourceTime, &notes, &valuesJSON); err != nil {
		if err == pgx.ErrNoRows {
			return nil, nil
		}
		return nil, err
	}
	vals := map[string]float64{}
	if valuesJSON != nil {
		if err := json.Unmarshal(valuesJSON, &vals); err != nil {
			return nil, err
		}
	}
	var sd *time.Time
	var st *time.Time
	_ = sd
	_ = st
	// sourceDate and sourceTime are optional textual fields from the original dataset; leave them nil for now
	return &domain.ReadingDetail{ID: rid, DeviceID: deviceID, RecordedAt: recordedAt, Notes: coalesceString(notes), Values: vals}, nil
}

func coalesceString(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

func (r *PgxRepository) CreateReading(ctx context.Context, input domain.CreateReadingInput) (int64, error) {
	tx, err := r.pool.Begin(ctx)
	if err != nil {
		return 0, err
	}
	defer tx.Rollback(ctx)

	var readingID int64
	if err := tx.QueryRow(ctx, "INSERT INTO readings (device_id, recorded_at, notes) VALUES ($1, $2, $3) RETURNING id", input.DeviceID, input.RecordedAt, input.Notes).Scan(&readingID); err != nil {
		return 0, err
	}

	// insert values
	for code, value := range input.Values {
		var sensorID int64
		if err := tx.QueryRow(ctx, "SELECT id FROM sensor_types WHERE code = $1", code).Scan(&sensorID); err != nil {
			if err == pgx.ErrNoRows {
				return 0, fmt.Errorf("unknown sensor code: %s", code)
			}
			return 0, err
		}
		if _, err := tx.Exec(ctx, "INSERT INTO reading_values (reading_id, sensor_type_id, numeric_value) VALUES ($1, $2, $3)", readingID, sensorID, value); err != nil {
			return 0, err
		}
	}

	if err := tx.Commit(ctx); err != nil {
		return 0, err
	}
	return readingID, nil
}

func (r *PgxRepository) AggregateReadings(ctx context.Context, filter domain.AggregateFilter) ([]domain.AggregatePoint, error) {
	args := make([]any, 0)
	idx := 1
	// bucket minutes as first arg
	args = append(args, int(filter.BucketMinutes))
	// sensor code as second arg
	args = append(args, filter.SensorCode)
	idx = 3

	where := []string{"st.code = $2"}
	if filter.DeviceID != nil {
		where = append(where, fmt.Sprintf("r.device_id = $%d", idx))
		args = append(args, *filter.DeviceID)
		idx++
	}
	if filter.From != nil {
		where = append(where, fmt.Sprintf("r.recorded_at >= $%d", idx))
		args = append(args, *filter.From)
		idx++
	}
	if filter.To != nil {
		where = append(where, fmt.Sprintf("r.recorded_at <= $%d", idx))
		args = append(args, *filter.To)
		idx++
	}

	sql := fmt.Sprintf(`SELECT to_timestamp(floor(extract(epoch FROM r.recorded_at)/($1*60)) * ($1*60)) AT TIME ZONE 'UTC' as bucket_start, AVG(rv.numeric_value)::double precision as avg_value, MIN(rv.numeric_value) as min_value, MAX(rv.numeric_value) as max_value, COUNT(*) as samples FROM readings r JOIN reading_values rv ON rv.reading_id = r.id JOIN sensor_types st ON st.id = rv.sensor_type_id WHERE %s GROUP BY bucket_start ORDER BY bucket_start`, strings.Join(where, " AND "))

	rows, err := r.pool.Query(ctx, sql, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	res := make([]domain.AggregatePoint, 0)
	for rows.Next() {
		var bucket time.Time
		var avg, min, max float64
		var samples int64
		if err := rows.Scan(&bucket, &avg, &min, &max, &samples); err != nil {
			return nil, err
		}
		res = append(res, domain.AggregatePoint{BucketStart: bucket, AvgValue: avg, MinValue: min, MaxValue: max, Samples: samples})
	}
	return res, nil
}
