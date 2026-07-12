# GraphQL Service (Rust)

GraphQL API for the shared IoT Air Quality database, built with
[`async-graphql`](https://async-graphql.github.io/) on top of Actix-web, talking
to PostgreSQL through `sqlx`.

Unlike the REST and gRPC services, this service lets the **client choose exactly
which fields it wants**. A query for `{ id }` returns only `id` — the resolvers
for the other fields never run. This is the over-fetch avoidance the project sets
out to demonstrate (assignment requirement: *"omogućiti klijentu selektovanje
specifičnih polja"*).

## Architecture

```
HTTP POST /graphql  (Actix-web)
      │
      ▼
async-graphql engine  ── parses query + variables, validates against schema
      │
      ▼
Query / Mutation resolvers  (src/schema.rs)   ── one resolver method per field
      │
      ▼
DbPool  (src/db.rs, sqlx)  ── SQL over the normalized schema
      │
      ▼
PostgreSQL
```

- `src/main.rs` — Actix-web server, builds the schema, wires `/graphql` (POST) and
  a GraphQL Playground (GET `/graphql` and `/`).
- `src/schema.rs` — `QueryRoot`, `MutationRoot`, and the GraphQL object types.
  Each field is a lazily-evaluated resolver, so unselected fields cost nothing.
- `src/db.rs` — `sqlx` queries against the normalized tables.
- `src/models.rs` / `src/errors.rs` — row structs and error type.

## Data model (important)

The GraphQL types are a **flattened view** of the project's normalized schema
(`devices`, `readings`, `reading_values`, `sensor_types`). One `Reading` in
GraphQL corresponds to a single **(reading, sensor)** measurement:

| GraphQL field | Source |
|---|---|
| `id` | `readings.id * 1000 + sensor_types.id` (synthetic composite) |
| `deviceId` | `devices.external_id` (e.g. `air-quality-station-01`) |
| `sensorCode` / `sensorName` | `sensor_types.code` / `.label` |
| `measuredValue` | `reading_values.numeric_value` |
| `measuredAt` | `readings.recorded_at` (RFC3339 string) |
| `createdAt` | `readings.created_at` (RFC3339 string) |

Valid sensor codes are the catalog codes: `temperature`, `relative_humidity`,
`absolute_humidity`, `co_gt`, `no2_gt`, `nox_gt`, `c6h6_gt`, `pt08_s1_co`,
`pt08_s2_nmhc`, `pt08_s3_nox`, `pt08_s4_no2`, `pt08_s5_o3`, `nmhc_gt`.

## Schema

```graphql
type Query {
  listSensorTypes: [SensorType!]!
  listReadings(limit: Int = 20, offset: Int = 0): [Reading!]!
  getReading(id: Int!): Reading!
  aggregateReadings(sensorCode: String!, startDate: String, endDate: String): [AggregatePoint!]!
}

type Mutation {
  createReading(deviceId: String!, sensorCode: String!, measuredValue: Float!, measuredAt: String): Reading!
}

type Reading {
  id: Int!
  deviceId: String!
  sensorCode: String!
  sensorName: String!
  measuredValue: Float!
  measuredAt: String!
  createdAt: String!
}

type SensorType { id: Int!  sensorCode: String!  sensorName: String!  unit: String! }

type AggregatePoint { sensorCode: String!  measuredAt: String!  avgValue: Float!  minValue: Float!  maxValue: Float!  count: Int! }
```

## Endpoint

- **POST** `http://localhost:8000/graphql` — execute queries/mutations.
- **GET** `http://localhost:8000/graphql` (or `/`) — interactive GraphQL Playground.

### Field selection (over-fetch avoidance)

```graphql
# asks for two fields -> response contains exactly two fields
query {
  listReadings(limit: 2) {
    id
    measuredValue
  }
}
```

```json
{ "data": { "listReadings": [
  { "id": 9357013, "measuredValue": 0.5028 },
  { "id": 9357004, "measuredValue": 11.9 }
] } }
```

### Aggregate

```graphql
query {
  aggregateReadings(
    sensorCode: "temperature"
    startDate: "2004-03-10T00:00:00Z"
    endDate: "2004-03-20T00:00:00Z"
  ) {
    measuredAt
    avgValue
    minValue
    maxValue
    count
  }
}
```

### Create reading (mutation)

```graphql
mutation {
  createReading(
    deviceId: "air-quality-station-01"
    sensorCode: "temperature"
    measuredValue: 21.5
    measuredAt: "2026-05-17T00:00:00Z"
  ) {
    id
    sensorCode
    measuredValue
    createdAt
  }
}
```

Variables are supported in the standard way:

```json
{ "query": "query($l:Int!){ listReadings(limit:$l){ id sensorCode } }", "variables": { "l": 5 } }
```

## Build & run

```bash
# via Docker Compose (from project-one/)
docker compose up --build graphql-service-rust

# locally
export DATABASE_URL=postgresql://iot_user:iot_password@localhost:5432/iot_project
cargo run
```

## Environment variables

- `DATABASE_URL` — full PostgreSQL connection string (preferred).
- `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` / `POSTGRES_HOST` — fallback
  parts used to build the URL when `DATABASE_URL` is unset.
- `RUST_LOG` — log level (default `info`).

## Error handling

async-graphql validates every request against the schema, so unknown fields or
wrong argument types are rejected before any resolver runs:

```json
{ "data": null, "errors": [ { "message": "Unknown field \"nope\" on type \"Reading\"." } ] }
```

Resolver-level failures (bad date format, unknown sensor code, limit out of range,
database errors) are returned as GraphQL `errors` with a descriptive message.

## Comparison with REST / gRPC

| Aspect | GraphQL | REST | gRPC |
|---|---|---|---|
| Transport | HTTP/1.1 | HTTP/1.1 | HTTP/2 |
| Payload | JSON | JSON | Protobuf (binary) |
| Field selection | Yes (per query) | No (fixed shape) | No (fixed message) |
| Over-fetching | Avoided | Possible | Message-shaped |
| Single endpoint | Yes | No (many routes) | No (many methods) |
| Schema/contract | GraphQL SDL | OpenAPI | `.proto` |

See [`../docs/protocol-comparison.md`](../docs/protocol-comparison.md) for the full
analysis and measured payload sizes.

## Related documentation

- [Project README](../README.md)
- [Architecture & diagrams](../docs/architecture.md)
- [REST service](../rest-service-csharp/README.md) · [gRPC service](../grpc-service-go/README.md)
