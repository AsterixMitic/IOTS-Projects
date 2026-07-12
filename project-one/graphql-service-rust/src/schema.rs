//! GraphQL schema for the IoT readings API.
//!
//! Every field is a lazily-evaluated resolver method, so a client that selects
//! only `id` never pays to serialize `sensorName`, `measuredAt`, etc. This is
//! what gives GraphQL its selective-monitoring advantage over REST/gRPC: the
//! server does the minimal work for exactly the fields the client asked for
//! (no over-fetching).

use std::sync::Arc;

use async_graphql::{Context, EmptySubscription, Object, Schema};
use chrono::{DateTime, Utc};

use crate::db::DbPool;
use crate::models::{AggregatePoint, Reading, SensorType};

/// A catalog entry describing one measured quantity (temperature, NO2, ...).
pub struct SensorTypeGql(pub SensorType);

#[Object(name = "SensorType")]
impl SensorTypeGql {
    async fn id(&self) -> i32 {
        self.0.id as i32
    }
    async fn sensor_code(&self) -> &str {
        &self.0.sensor_code
    }
    async fn sensor_name(&self) -> &str {
        &self.0.sensor_name
    }
    async fn unit(&self) -> &str {
        &self.0.unit
    }
}

/// A single (reading, sensor) measurement flattened from the normalized schema.
pub struct ReadingGql(pub Reading);

#[Object(name = "Reading")]
impl ReadingGql {
    async fn id(&self) -> i32 {
        self.0.id as i32
    }
    async fn device_id(&self) -> &str {
        &self.0.device_id
    }
    async fn sensor_code(&self) -> &str {
        &self.0.sensor_code
    }
    async fn sensor_name(&self) -> &str {
        &self.0.sensor_name
    }
    async fn measured_value(&self) -> f64 {
        self.0.measured_value
    }
    async fn measured_at(&self) -> String {
        self.0.measured_at.to_rfc3339()
    }
    async fn created_at(&self) -> String {
        self.0.created_at.to_rfc3339()
    }
}

/// A time-bucketed aggregate over one sensor's values.
pub struct AggregatePointGql(pub AggregatePoint);

#[Object(name = "AggregatePoint")]
impl AggregatePointGql {
    async fn sensor_code(&self) -> &str {
        &self.0.sensor_code
    }
    async fn measured_at(&self) -> String {
        self.0.measured_at.to_rfc3339()
    }
    async fn avg_value(&self) -> f64 {
        self.0.avg_value
    }
    async fn min_value(&self) -> f64 {
        self.0.min_value
    }
    async fn max_value(&self) -> f64 {
        self.0.max_value
    }
    async fn count(&self) -> i32 {
        self.0.count as i32
    }
}

fn parse_rfc3339(label: &str, value: &str) -> async_graphql::Result<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(value)
        .map(|dt| dt.with_timezone(&Utc))
        .map_err(|_| async_graphql::Error::new(format!("Invalid {label} format (expected RFC3339)")))
}

pub struct QueryRoot;

#[Object]
impl QueryRoot {
    /// List the sensor-type catalog.
    async fn list_sensor_types(&self, ctx: &Context<'_>) -> async_graphql::Result<Vec<SensorTypeGql>> {
        let db = ctx.data_unchecked::<Arc<DbPool>>();
        let rows = db
            .list_sensor_types()
            .await
            .map_err(|e| async_graphql::Error::new(e.to_string()))?;
        Ok(rows.into_iter().map(SensorTypeGql).collect())
    }

    /// List measurements with pagination. Clients pick exactly the fields they
    /// need, so a "give me only id + measuredAt" query avoids over-fetching.
    async fn list_readings(
        &self,
        ctx: &Context<'_>,
        #[graphql(default = 20)] limit: i32,
        #[graphql(default = 0)] offset: i32,
    ) -> async_graphql::Result<Vec<ReadingGql>> {
        if !(1..=100).contains(&limit) {
            return Err(async_graphql::Error::new("limit must be between 1 and 100"));
        }
        if offset < 0 {
            return Err(async_graphql::Error::new("offset must be >= 0"));
        }

        let db = ctx.data_unchecked::<Arc<DbPool>>();
        let rows = db
            .list_readings(limit, offset)
            .await
            .map_err(|e| async_graphql::Error::new(e.to_string()))?;
        Ok(rows.into_iter().map(ReadingGql).collect())
    }

    /// Fetch a single measurement by its composite id.
    async fn get_reading(&self, ctx: &Context<'_>, id: i32) -> async_graphql::Result<ReadingGql> {
        let db = ctx.data_unchecked::<Arc<DbPool>>();
        let reading = db
            .get_reading(id as i64)
            .await
            .map_err(|e| async_graphql::Error::new(e.to_string()))?;
        Ok(ReadingGql(reading))
    }

    /// Daily aggregates (avg/min/max/count) for one sensor over a date range.
    async fn aggregate_readings(
        &self,
        ctx: &Context<'_>,
        sensor_code: String,
        start_date: Option<String>,
        end_date: Option<String>,
    ) -> async_graphql::Result<Vec<AggregatePointGql>> {
        let start = match start_date {
            Some(value) => parse_rfc3339("start_date", &value)?,
            None => parse_rfc3339("start_date", "2004-01-01T00:00:00Z")?,
        };
        let end = match end_date {
            Some(value) => parse_rfc3339("end_date", &value)?,
            None => Utc::now(),
        };

        let db = ctx.data_unchecked::<Arc<DbPool>>();
        let rows = db
            .aggregate_readings(&sensor_code, start, end)
            .await
            .map_err(|e| async_graphql::Error::new(e.to_string()))?;
        Ok(rows.into_iter().map(AggregatePointGql).collect())
    }
}

pub struct MutationRoot;

#[Object]
impl MutationRoot {
    /// Ingest a single measurement for an existing device + sensor code.
    async fn create_reading(
        &self,
        ctx: &Context<'_>,
        device_id: String,
        sensor_code: String,
        measured_value: f64,
        measured_at: Option<String>,
    ) -> async_graphql::Result<ReadingGql> {
        let measured_at = match measured_at {
            Some(value) => parse_rfc3339("measured_at", &value)?,
            None => Utc::now(),
        };

        let db = ctx.data_unchecked::<Arc<DbPool>>();
        let reading = db
            .create_reading(&device_id, &sensor_code, measured_value, measured_at)
            .await
            .map_err(|e| async_graphql::Error::new(e.to_string()))?;
        Ok(ReadingGql(reading))
    }
}

pub type AppSchema = Schema<QueryRoot, MutationRoot, EmptySubscription>;
