use chrono::Utc;
use sqlx::postgres::PgPoolOptions;
use sqlx::{query_scalar, PgPool};

use crate::errors::{ServiceError, ServiceResult};
use crate::models::{AggregatePoint, Reading, SensorType};

#[derive(Clone)]
pub struct DbPool {
    pool: PgPool,
}

impl DbPool {
    pub async fn new(database_url: &str) -> ServiceResult<Self> {
        let pool = PgPoolOptions::new()
            .max_connections(20)
            .connect(database_url)
            .await
            .map_err(|e| ServiceError::DatabaseError(e.to_string()))?;

        Ok(DbPool { pool })
    }

    pub async fn list_sensor_types(&self) -> ServiceResult<Vec<SensorType>> {
        sqlx::query_as::<_, SensorType>(
            r#"
            SELECT
                id,
                code AS sensor_code,
                label AS sensor_name,
                unit
            FROM sensor_types
            ORDER BY code
            "#,
        )
        .fetch_all(&self.pool)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))
    }

    pub async fn list_readings(&self, limit: i32, offset: i32) -> ServiceResult<Vec<Reading>> {
        sqlx::query_as::<_, Reading>(
            r#"
            SELECT
                ((r.id * 1000) + st.id)::bigint AS id,
                d.external_id AS device_id,
                st.code AS sensor_code,
                st.label AS sensor_name,
                COALESCE(rv.numeric_value, 0)::float8 AS measured_value,
                r.recorded_at AS measured_at,
                r.created_at AS created_at
            FROM readings r
            JOIN devices d ON d.id = r.device_id
            JOIN reading_values rv ON rv.reading_id = r.id
            JOIN sensor_types st ON st.id = rv.sensor_type_id
            ORDER BY r.created_at DESC, r.id DESC, st.code
            LIMIT $1 OFFSET $2
            "#,
        )
        .bind(limit)
        .bind(offset)
        .fetch_all(&self.pool)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))
    }

    pub async fn get_reading(&self, id: i64) -> ServiceResult<Reading> {
        sqlx::query_as::<_, Reading>(
            r#"
            SELECT
                ((r.id * 1000) + st.id)::bigint AS id,
                d.external_id AS device_id,
                st.code AS sensor_code,
                st.label AS sensor_name,
                COALESCE(rv.numeric_value, 0)::float8 AS measured_value,
                r.recorded_at AS measured_at,
                r.created_at AS created_at
            FROM readings r
            JOIN devices d ON d.id = r.device_id
            JOIN reading_values rv ON rv.reading_id = r.id
            JOIN sensor_types st ON st.id = rv.sensor_type_id
            WHERE ((r.id * 1000) + st.id)::bigint = $1
            "#,
        )
        .bind(id)
        .fetch_optional(&self.pool)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))?
        .ok_or(ServiceError::NotFound)
    }

    pub async fn create_reading(
        &self,
        device_id: &str,
        sensor_code: &str,
        measured_value: f64,
        measured_at: chrono::DateTime<Utc>,
    ) -> ServiceResult<Reading> {
        let normalized_code = sensor_code.to_lowercase();
        let mut tx = self
            .pool
            .begin()
            .await
            .map_err(|e| ServiceError::DatabaseError(e.to_string()))?;

        let device_pk: i64 = query_scalar::<_, i64>("SELECT id FROM devices WHERE external_id = $1")
            .bind(device_id)
            .fetch_optional(&mut *tx)
            .await
            .map_err(|e| ServiceError::DatabaseError(e.to_string()))?
            .ok_or_else(|| ServiceError::InvalidArgument(format!("Unknown device: {device_id}")))?;

        let sensor_type: (i64, String) = sqlx::query_as(
            "SELECT id, label FROM sensor_types WHERE code = $1",
        )
        .bind(&normalized_code)
        .fetch_optional(&mut *tx)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))?
        .ok_or_else(|| ServiceError::InvalidArgument(format!("Invalid sensor code: {sensor_code}")))?;

        let sensor_type_id = sensor_type.0;
        let sensor_name = sensor_type.1;

        let inserted_reading_id: i64 = query_scalar::<_, i64>(
            r#"
            INSERT INTO readings (device_id, recorded_at, notes)
            VALUES ($1, $2, 'Created via GraphQL')
            RETURNING id
            "#,
        )
        .bind(device_pk)
        .bind(measured_at)
        .fetch_one(&mut *tx)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))?;

        sqlx::query(
            "INSERT INTO reading_values (reading_id, sensor_type_id, numeric_value) VALUES ($1, $2, $3)",
        )
        .bind(inserted_reading_id)
        .bind(sensor_type_id)
        .bind(measured_value)
        .execute(&mut *tx)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))?;

        let reading = sqlx::query_as::<_, Reading>(
            r#"
            SELECT
                ((r.id * 1000) + st.id)::bigint AS id,
                d.external_id AS device_id,
                st.code AS sensor_code,
                st.label AS sensor_name,
                COALESCE(rv.numeric_value, 0)::float8 AS measured_value,
                r.recorded_at AS measured_at,
                r.created_at AS created_at
            FROM readings r
            JOIN devices d ON d.id = r.device_id
            JOIN reading_values rv ON rv.reading_id = r.id
            JOIN sensor_types st ON st.id = rv.sensor_type_id
            WHERE r.id = $1 AND st.id = $2
            "#,
        )
        .bind(inserted_reading_id)
        .bind(sensor_type_id)
        .fetch_one(&mut *tx)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))?;

        tx.commit()
            .await
            .map_err(|e| ServiceError::DatabaseError(e.to_string()))?;

        Ok(reading)
    }

    pub async fn aggregate_readings(
        &self,
        sensor_code: &str,
        start_date: chrono::DateTime<Utc>,
        end_date: chrono::DateTime<Utc>,
    ) -> ServiceResult<Vec<AggregatePoint>> {
        let normalized_code = sensor_code.to_lowercase();

        sqlx::query_as::<_, AggregatePoint>(
            r#"
            SELECT
                st.code AS sensor_code,
                DATE_TRUNC('day', r.recorded_at) AS measured_at,
                AVG(rv.numeric_value)::float8 AS avg_value,
                MIN(rv.numeric_value)::float8 AS min_value,
                MAX(rv.numeric_value)::float8 AS max_value,
                COUNT(*)::bigint AS count
            FROM readings r
            JOIN reading_values rv ON rv.reading_id = r.id
            JOIN sensor_types st ON st.id = rv.sensor_type_id
            WHERE st.code = $1
              AND r.recorded_at >= $2
              AND r.recorded_at < $3
            GROUP BY st.code, DATE_TRUNC('day', r.recorded_at)
            ORDER BY measured_at DESC
            "#,
        )
        .bind(&normalized_code)
        .bind(start_date)
        .bind(end_date)
        .fetch_all(&self.pool)
        .await
        .map_err(|e| ServiceError::DatabaseError(e.to_string()))
    }
}
