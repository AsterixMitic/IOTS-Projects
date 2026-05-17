use juniper::{RootNode, graphql_object, EmptyMutation};
use chrono::{DateTime, Utc};
use crate::db::DbPool;
use crate::models::{SensorType, Reading, AggregatePoint};
use std::sync::Arc;

pub struct Context {
    pub db: Arc<DbPool>,
}

impl juniper::Context for Context {}

#[graphql_object(context = Context)]
impl SensorType {
    fn id(&self) -> i32 {
        self.id
    }
    fn sensor_code(&self) -> &str {
        &self.sensor_code
    }
    fn sensor_name(&self) -> &str {
        &self.sensor_name
    }
    fn unit(&self) -> &str {
        &self.unit
    }
}

#[graphql_object(context = Context)]
impl Reading {
    fn id(&self) -> i32 {
        self.id
    }
    fn device_id(&self) -> &str {
        &self.device_id
    }
    fn sensor_code(&self) -> &str {
        &self.sensor_code
    }
    fn sensor_name(&self) -> &str {
        &self.sensor_name
    }
    fn measured_value(&self) -> f64 {
        self.measured_value
    }
    fn measured_at(&self) -> DateTime<Utc> {
        self.measured_at
    }
    fn created_at(&self) -> DateTime<Utc> {
        self.created_at
    }
}

#[graphql_object(context = Context)]
impl AggregatePoint {
    fn sensor_code(&self) -> &str {
        &self.sensor_code
    }
    fn measured_at(&self) -> DateTime<Utc> {
        self.measured_at
    }
    fn avg_value(&self) -> f64 {
        self.avg_value
    }
    fn min_value(&self) -> f64 {
        self.min_value
    }
    fn max_value(&self) -> f64 {
        self.max_value
    }
    fn count(&self) -> i64 {
        self.count
    }
}

pub struct Query;

#[graphql_object(context = Context)]
impl Query {
    async fn list_sensor_types(context: &Context) -> Result<Vec<SensorType>, String> {
        context.db
            .list_sensor_types()
            .await
            .map_err(|e| e.to_string())
    }

    async fn list_readings(context: &Context, #[graphql(default = 20)] limit: i32, #[graphql(default = 0)] offset: i32) -> Result<Vec<Reading>, String> {
        if limit < 1 || limit > 100 {
            return Err("limit must be between 1 and 100".to_string());
        }
        if offset < 0 {
            return Err("offset must be >= 0".to_string());
        }

        context.db
            .list_readings(limit, offset)
            .await
            .map_err(|e| e.to_string())
    }

    async fn get_reading(context: &Context, id: i32) -> Result<Reading, String> {
        context.db
            .get_reading(id)
            .await
            .map_err(|e| e.to_string())
    }

    async fn aggregate_readings(
        context: &Context,
        sensor_code: String,
        #[graphql(default = "2023-01-01T00:00:00Z")] start_date: String,
        #[graphql(default)] end_date: Option<String>,
    ) -> Result<Vec<AggregatePoint>, String> {
        let start = DateTime::parse_from_rfc3339(&start_date)
            .map_err(|_| "Invalid start_date format".to_string())?
            .with_timezone(&Utc);

        let end = if let Some(end_str) = end_date {
            DateTime::parse_from_rfc3339(&end_str)
                .map_err(|_| "Invalid end_date format".to_string())?
                .with_timezone(&Utc)
        } else {
            Utc::now()
        };

        context.db
            .aggregate_readings(&sensor_code, start, end)
            .await
            .map_err(|e| e.to_string())
    }
}

pub struct Mutation;

#[graphql_object(context = Context)]
impl Mutation {
    async fn create_reading(
        context: &Context,
        device_id: String,
        sensor_code: String,
        measured_value: f64,
        #[graphql(default)] measured_at: Option<String>,
    ) -> Result<Reading, String> {
        let measured_at = if let Some(dt_str) = measured_at {
            DateTime::parse_from_rfc3339(&dt_str)
                .map_err(|_| "Invalid measured_at format".to_string())?
                .with_timezone(&Utc)
        } else {
            Utc::now()
        };

        context.db
            .create_reading(&device_id, &sensor_code, measured_value, measured_at)
            .await
            .map_err(|e| e.to_string())
    }
}

pub type Schema = RootNode<'static, Query, Mutation>;
