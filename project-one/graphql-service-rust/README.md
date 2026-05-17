# GraphQL Service (Rust)

High-performance GraphQL API implementation for IoT sensor data using Rust, async-graphql, and Actix-web.

## Architecture

The service follows a clean, layered architecture:

```
HTTP Request (Actix-web)
    ↓
GraphQL Query (async-graphql)
    ↓
Query/Mutation Resolvers (schema.rs)
    ↓
Database Layer (db.rs, using sqlx)
    ↓
PostgreSQL
```

## Features

- **Query Interface**:
  - `listSensorTypes` - Get all sensor types
  - `listReadings` - Get readings with pagination (limit, offset)
  - `getReading` - Get a specific reading by ID
  - `aggregateReadings` - Get daily aggregated readings for a sensor

- **Mutation Interface**:
  - `createReading` - Create a new sensor reading

- **Error Handling**: GraphQL-compliant error responses with validation

- **Performance**: Uses connection pooling (20 max connections) for efficient database access

## Dependencies

- `async-graphql` - GraphQL server framework
- `actix-web` - Web server
- `sqlx` - Async SQL toolkit with Postgres support
- `tokio` - Async runtime
- `chrono` - Date/time handling
- `serde` - JSON serialization

## Building

```bash
cargo build --release
```

## Running Locally

```bash
# Set environment variables
export DATABASE_URL=postgresql://user:password@localhost:5432/iot_db
export RUST_LOG=debug

# Run service
cargo run
```

Service starts on `http://0.0.0.0:8000`

## Running in Docker

```bash
docker build -t graphql-service-rust .
docker run -p 8000:8000 \
  -e DATABASE_URL=postgresql://user:password@postgres:5432/iot_db \
  graphql-service-rust
```

## GraphQL Endpoint

**URL**: `http://localhost:8000/graphql`

### Query Examples

**Get all sensor types**:
```graphql
query {
  listSensorTypes {
    id
    sensorCode
    sensorName
    unit
  }
}
```

**Get paginated readings**:
```graphql
query {
  listReadings(limit: 10, offset: 0) {
    id
    deviceId
    sensorCode
    sensorName
    measuredValue
    measuredAt
    createdAt
  }
}
```

**Get specific reading**:
```graphql
query {
  getReading(id: 1) {
    id
    deviceId
    sensorCode
    measuredValue
  }
}
```

**Aggregate readings by day**:
```graphql
query {
  aggregateReadings(
    sensorCode: "NO2"
    startDate: "2023-01-01T00:00:00Z"
    endDate: "2023-01-31T23:59:59Z"
  ) {
    sensorCode
    measuredAt
    avgValue
    minValue
    maxValue
    count
  }
}
```

### Mutation Examples

**Create reading**:
```graphql
mutation {
  createReading(
    deviceId: "station-001"
    sensorCode: "NO2"
    measuredValue: 45.2
    measuredAt: "2023-12-20T14:30:00Z"
  ) {
    id
    deviceId
    sensorCode
    measuredValue
    createdAt
  }
}
```

## Database Schema

The service expects these tables:

```sql
-- sensor_types table
CREATE TABLE sensor_types (
  id SERIAL PRIMARY KEY,
  sensor_code VARCHAR(50) UNIQUE NOT NULL,
  sensor_name VARCHAR(255) NOT NULL,
  unit VARCHAR(50) NOT NULL
);

-- readings table
CREATE TABLE readings (
  id SERIAL PRIMARY KEY,
  device_id VARCHAR(100) NOT NULL,
  sensor_code VARCHAR(50) NOT NULL,
  sensor_name VARCHAR(255) NOT NULL,
  measured_value FLOAT NOT NULL,
  measured_at TIMESTAMP WITH TIME ZONE NOT NULL,
  created_at TIMESTAMP WITH TIME ZONE NOT NULL
);
```

## Error Handling

Common error responses:

- **InvalidArgument**: Input validation failed (e.g., limit > 100)
- **NotFound**: Resource doesn't exist
- **DuplicateSensorCode**: Sensor code collision after normalization
- **DatabaseError**: Database operation failed
- **InternalServerError**: Unexpected server error

Example error response:
```json
{
  "errors": [
    {
      "message": "limit must be between 1 and 100"
    }
  ]
}
```

## Environment Variables

- `RUST_LOG` - Log level (default: info)
- `DATABASE_URL` - PostgreSQL connection string
- `POSTGRES_USER` - Fallback database user
- `POSTGRES_PASSWORD` - Fallback database password
- `POSTGRES_DB` - Fallback database name
- `POSTGRES_HOST` - Fallback database host (default: localhost)

## Performance Characteristics

- Connection pool: 20 concurrent connections
- Single GraphQL endpoint reduces over-fetching
- async-graphql compiles to efficient resolver code
- Tokio runtime provides high concurrency
- SQLx supports prepared statements and query caching

## Comparison with REST/gRPC

| Aspect | GraphQL | REST | gRPC |
|---|---|---|---|
| Protocol | HTTP | HTTP | HTTP/2 |
| Payload | JSON | JSON | Protobuf |
| Overfetch | No | Yes | No |
| Query Flexibility | High | Low | Low |
| Learning Curve | Moderate | Low | High |
| Tooling | Growing | Excellent | Good |

## Related Documentation

- [Project README](../README.md) - Main project overview
- [REST Service](../rest-service-csharp/README.md) - C# REST implementation
- [gRPC Service](../grpc-service-go/README.md) - Go gRPC implementation
