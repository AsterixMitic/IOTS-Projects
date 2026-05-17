use actix_web::{middleware, web, App, HttpResponse, HttpServer};
use serde::Deserialize;
use serde_json::{json, Value};
use tracing_subscriber;

mod db;
mod models;
mod errors;

use db::DbPool;
use models::{SensorType, Reading, AggregatePoint};
use chrono::Utc;

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    tracing_subscriber::fmt::init();

    dotenv::dotenv().ok();

    // Initialize database pool
    let db_url = std::env::var("DATABASE_URL").unwrap_or_else(|_| {
        let user = std::env::var("POSTGRES_USER").unwrap_or_else(|_| "root".to_string());
        let password = std::env::var("POSTGRES_PASSWORD").unwrap_or_else(|_| "root".to_string());
        let db = std::env::var("POSTGRES_DB").unwrap_or_else(|_| "iot_db".to_string());
        let host = std::env::var("POSTGRES_HOST").unwrap_or_else(|_| "localhost".to_string());
        format!("postgresql://{}:{}@{}/{}", user, password, host, db)
    });

    tracing::info!("Connecting to database: {}", &db_url);
    
    let pool = DbPool::new(&db_url)
        .await
        .expect("Failed to create database pool");

    tracing::info!("Database pool created successfully");

    let db_pool = web::Data::new(pool);

    tracing::info!("Starting GraphQL service on 0.0.0.0:8000");

    HttpServer::new(move || {
        App::new()
            .app_data(db_pool.clone())
            .wrap(middleware::Logger::default())
            .route("/graphql", web::post().to(graphql_handler))
            .route("/", web::get().to(index))
    })
    .bind("0.0.0.0:8000")?
    .run()
    .await
}

#[derive(Deserialize)]
struct GraphQLRequest {
    query: String,
    variables: Option<Value>,
}

async fn graphql_handler(
    db_pool: web::Data<DbPool>,
    payload: web::Bytes,
) -> HttpResponse {
    let request = match parse_graphql_request(&payload) {
        Ok(request) => request,
        Err(message) => {
            return HttpResponse::BadRequest().json(json!({
                "errors": [{"message": message}]
            }));
        }
    };

    let GraphQLRequest { query, variables } = request;
    let _ = variables;
    let query = query.trim();
    let db = db_pool.get_ref();
    
    // Simple query parsing
    match query {
        q if q.contains("listSensorTypes") => {
            match db.list_sensor_types().await {
                Ok(sensors) => {
                    let data = json!({
                        "listSensorTypes": sensors.iter().map(|s| json!({
                            "id": s.id,
                            "sensorCode": s.sensor_code,
                            "sensorName": s.sensor_name,
                            "unit": s.unit
                        })).collect::<Vec<_>>()
                    });
                    HttpResponse::Ok().json(json!({"data": data}))
                },
                Err(e) => {
                    HttpResponse::Ok().json(json!({
                        "errors": [{"message": e.to_string()}]
                    }))
                }
            }
        },
        q if q.contains("listReadings") => {
            let limit = extract_int_arg(query, "limit")
                .and_then(|value| i32::try_from(value).ok())
                .unwrap_or(20);
            let offset = extract_int_arg(query, "offset")
                .and_then(|value| i32::try_from(value).ok())
                .unwrap_or(0);
            
            if limit < 1 || limit > 100 || offset < 0 {
                return HttpResponse::Ok().json(json!({
                    "errors": [{"message": "Invalid limit or offset"}]
                }));
            }
            
            match db.list_readings(limit, offset).await {
                Ok(readings) => {
                    let data = json!({
                        "listReadings": readings.iter().map(|r| json!({
                            "id": r.id,
                            "deviceId": r.device_id,
                            "sensorCode": r.sensor_code,
                            "sensorName": r.sensor_name,
                            "measuredValue": r.measured_value,
                            "measuredAt": r.measured_at.to_rfc3339(),
                            "createdAt": r.created_at.to_rfc3339()
                        })).collect::<Vec<_>>()
                    });
                    HttpResponse::Ok().json(json!({"data": data}))
                },
                Err(e) => {
                    HttpResponse::Ok().json(json!({
                        "errors": [{"message": e.to_string()}]
                    }))
                }
            }
        },
        q if q.contains("getReading") => {
            if let Some(id) = extract_i64_arg(query, "id") {
                match db.get_reading(id).await {
                    Ok(reading) => {
                        let data = json!({
                            "getReading": json!({
                                "id": reading.id,
                                "deviceId": reading.device_id,
                                "sensorCode": reading.sensor_code,
                                "sensorName": reading.sensor_name,
                                "measuredValue": reading.measured_value,
                                "measuredAt": reading.measured_at.to_rfc3339(),
                                "createdAt": reading.created_at.to_rfc3339()
                            })
                        });
                        HttpResponse::Ok().json(json!({"data": data}))
                    },
                    Err(e) => {
                        HttpResponse::Ok().json(json!({
                            "errors": [{"message": e.to_string()}]
                        }))
                    }
                }
            } else {
                HttpResponse::Ok().json(json!({
                    "errors": [{"message": "Missing id argument"}]
                }))
            }
        },
        q if q.contains("aggregateReadings") => {
            if let Some(sensor_code) = extract_string_arg(query, "sensorCode") {
                let start_date = extract_string_arg(query, "startDate").unwrap_or_else(|| "2023-01-01T00:00:00Z".to_string());
                let end_date = extract_string_arg(query, "endDate");
                
                let start = chrono::DateTime::parse_from_rfc3339(&start_date)
                    .map(|dt| dt.with_timezone(&Utc));
                let end = if let Some(end_str) = end_date {
                    chrono::DateTime::parse_from_rfc3339(&end_str)
                        .map(|dt| dt.with_timezone(&Utc))
                } else {
                    Ok(Utc::now())
                };
                
                match (start, end) {
                    (Ok(start), Ok(end)) => {
                        match db.aggregate_readings(&sensor_code, start, end).await {
                            Ok(aggregates) => {
                                let data = json!({
                                    "aggregateReadings": aggregates.iter().map(|a| json!({
                                        "sensorCode": a.sensor_code,
                                        "measuredAt": a.measured_at.to_rfc3339(),
                                        "avgValue": a.avg_value,
                                        "minValue": a.min_value,
                                        "maxValue": a.max_value,
                                        "count": a.count
                                    })).collect::<Vec<_>>()
                                });
                                HttpResponse::Ok().json(json!({"data": data}))
                            },
                            Err(e) => {
                                HttpResponse::Ok().json(json!({
                                    "errors": [{"message": e.to_string()}]
                                }))
                            }
                        }
                    },
                    _ => {
                        HttpResponse::Ok().json(json!({
                            "errors": [{"message": "Invalid date format"}]
                        }))
                    }
                }
            } else {
                HttpResponse::Ok().json(json!({
                    "errors": [{"message": "Missing sensorCode argument"}]
                }))
            }
        },
        q if q.contains("createReading") => {
            if let (Some(device_id), Some(sensor_code), Some(measured_value)) = (
                extract_string_arg(query, "deviceId"),
                extract_string_arg(query, "sensorCode"),
                extract_float_arg(query, "measuredValue")
            ) {
                let measured_at = if let Some(dt_str) = extract_string_arg(query, "measuredAt") {
                    chrono::DateTime::parse_from_rfc3339(&dt_str)
                        .map(|dt| dt.with_timezone(&Utc))
                        .unwrap_or_else(|_| Utc::now())
                } else {
                    Utc::now()
                };
                
                match db.create_reading(&device_id, &sensor_code, measured_value, measured_at).await {
                    Ok(reading) => {
                        let data = json!({
                            "createReading": json!({
                                "id": reading.id,
                                "deviceId": reading.device_id,
                                "sensorCode": reading.sensor_code,
                                "sensorName": reading.sensor_name,
                                "measuredValue": reading.measured_value,
                                "measuredAt": reading.measured_at.to_rfc3339(),
                                "createdAt": reading.created_at.to_rfc3339()
                            })
                        });
                        HttpResponse::Ok().json(json!({"data": data}))
                    },
                    Err(e) => {
                        HttpResponse::Ok().json(json!({
                            "errors": [{"message": e.to_string()}]
                        }))
                    }
                }
            } else {
                HttpResponse::Ok().json(json!({
                    "errors": [{"message": "Missing required arguments"}]
                }))
            }
        },
        _ => {
            HttpResponse::Ok().json(json!({
                "errors": [{"message": "Unknown query"}]
            }))
        }
    }
}

fn parse_graphql_request(payload: &[u8]) -> Result<GraphQLRequest, String> {
    if let Ok(request) = serde_json::from_slice::<GraphQLRequest>(payload) {
        return Ok(request);
    }

    let body = std::str::from_utf8(payload).map_err(|e| e.to_string())?;
    let trimmed = body.trim();

    if trimmed.contains("\\\"") {
        let unescaped = trimmed.replace("\\\"", "\"");
        if let Ok(request) = serde_json::from_str::<GraphQLRequest>(&unescaped) {
            return Ok(request);
        }
    }

    Ok(GraphQLRequest {
        query: trimmed.to_string(),
        variables: None,
    })
}

fn extract_int_arg(query: &str, name: &str) -> Option<i64> {
    let start = query.find(&format!("{}:", name))?;
    let substr = &query[start..];
    let colon_pos = substr.find(':')?;
    let after_colon = &substr[colon_pos + 1..].trim_start();
    let end = after_colon.find(|c: char| !c.is_ascii_digit()).unwrap_or(after_colon.len());
    after_colon[..end].trim().parse().ok()
}

fn extract_i64_arg(query: &str, name: &str) -> Option<i64> {
    extract_int_arg(query, name)
}

fn extract_float_arg(query: &str, name: &str) -> Option<f64> {
    let start = query.find(&format!("{}:", name))?;
    let substr = &query[start..];
    let colon_pos = substr.find(':')?;
    let after_colon = &substr[colon_pos + 1..].trim_start();
    let end = after_colon.find(|c: char| !c.is_ascii_digit() && c != '.').unwrap_or(after_colon.len());
    after_colon[..end].trim().parse().ok()
}

fn extract_string_arg(query: &str, name: &str) -> Option<String> {
    let start = query.find(&format!("{}:", name))?;
    let substr = &query[start..];
    let colon_pos = substr.find(':')?;
    let after_colon = &substr[colon_pos + 1..].trim_start();
    let quote_start = after_colon.find('"')?;
    let after_quote = &after_colon[quote_start + 1..];
    let quote_end = after_quote.find('"')?;
    Some(after_quote[..quote_end].to_string())
}

async fn index() -> HttpResponse {
    HttpResponse::Ok().json(json!({
        "message": "GraphQL service running",
        "graphql_endpoint": "/graphql",
        "description": "IoT Air Quality GraphQL API"
    }))
}
