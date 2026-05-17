use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use sqlx::FromRow;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, FromRow)]
pub struct SensorType {
    pub id: i64,
    pub sensor_code: String,
    pub sensor_name: String,
    pub unit: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, FromRow)]
pub struct Reading {
    pub id: i64,
    pub device_id: String,
    pub sensor_code: String,
    pub sensor_name: String,
    pub measured_value: f64,
    pub measured_at: DateTime<Utc>,
    pub created_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize, FromRow)]
pub struct AggregatePoint {
    pub sensor_code: String,
    pub measured_at: DateTime<Utc>,
    pub avg_value: f64,
    pub min_value: f64,
    pub max_value: f64,
    pub count: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CreateReadingInput {
    pub device_id: String,
    pub sensor_code: String,
    pub measured_value: f64,
    pub measured_at: DateTime<Utc>,
}
