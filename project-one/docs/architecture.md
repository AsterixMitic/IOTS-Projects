# System Architecture

Comparative study of three synchronous communication paradigms — **REST**, **gRPC**
and **GraphQL** — over one shared PostgreSQL database, in a polyglot, fully
containerized IoT microservice system. The dataset is the
[UCI Air Quality](https://archive.ics.uci.edu/dataset/360/air+quality) time series
(9,357 hourly readings, 13 sensors, 2004-03-10 → 2005-04-04).

All diagrams below are Mermaid (diagram-as-code, the source of truth). GitHub,
VS Code (Markdown Preview Mermaid Support) and most Markdown viewers render them
natively.

---

## 1. System / container architecture

How the whole system fits together: three protocol services share one database,
and a metrics pipeline observes every container.

```mermaid
flowchart TB
    subgraph clients["Clients / test harness"]
        K6["k6 load tests<br/>(10 / 100 / 500 VUs)"]
        PM["Postman<br/>(payload-size console)"]
        BR["Browser / Playground"]
    end

    subgraph services["Protocol services (polyglot)"]
        REST["REST service<br/>C# / ASP.NET Core<br/>:5000 → :8080<br/>JSON + OpenAPI"]
        GRPC["gRPC service<br/>Go<br/>:50051<br/>Protobuf / HTTP2"]
        GQL["GraphQL service<br/>Rust / async-graphql + Actix<br/>:8000<br/>field selection"]
    end

    PG[("PostgreSQL 16<br/>:5432<br/>normalized IoT schema")]

    subgraph obs["Observability"]
        EXP["docker-stats-exporter<br/>Python :9101"]
        PROM["Prometheus<br/>:9090"]
        GRAF["Grafana<br/>:3000"]
    end

    K6 -->|HTTP JSON| REST
    K6 -->|gRPC| GRPC
    K6 -->|HTTP JSON| GQL
    PM --> REST & GRPC & GQL
    BR --> GQL

    REST -->|EF Core / Npgsql| PG
    GRPC -->|pgx pool| PG
    GQL -->|sqlx pool| PG

    EXP -->|docker.sock stats| services
    PROM -->|scrape| EXP
    GRAF -->|query| PROM
```

**Design principles**

- **Service isolation** — each service is an independently deployable container.
- **Polyglot** — REST in C#, gRPC in Go, GraphQL in Rust (satisfies the "≥ 2
  technologies" requirement three times over).
- **Contract-based** — REST → OpenAPI, gRPC → `.proto`, GraphQL → schema.
- **Shared source of truth** — all three read/write the *same* normalized tables,
  so protocol behavior is compared on identical data.
- **Observability-first** — CPU/RAM of every container is scraped during load tests.

---

## 2. Database schema (ER diagram)

The schema is normalized: one `readings` row per timestamped observation, and one
`reading_values` row per (reading, sensor) measurement. It is optimized for IoT
access patterns via a composite `(device_id, recorded_at DESC)` index and a
`recorded_at DESC` index.

```mermaid
erDiagram
    devices ||--o{ readings : "records"
    readings ||--o{ reading_values : "has"
    sensor_types ||--o{ reading_values : "typed by"

    devices {
        bigserial id PK
        text external_id UK "e.g. air-quality-station-01"
        text name
        text location
        timestamptz created_at
    }
    sensor_types {
        bigserial id PK
        text code UK "e.g. temperature, no2_gt"
        text label
        text unit "celsius, ppb, ..."
    }
    readings {
        bigserial id PK
        bigint device_id FK
        timestamptz recorded_at "indexed DESC"
        date source_date
        time source_time
        text notes
        timestamptz created_at
    }
    reading_values {
        bigint reading_id PK "FK to readings"
        bigint sensor_type_id PK "FK to sensor_types"
        numeric numeric_value
        text text_value
    }
```

**Indexes** (from `db/0001_schema_and_staging.up.sql`)

| Index | Columns | Serves |
|---|---|---|
| `idx_readings_device_recorded_at` | `(device_id, recorded_at DESC)` | device-scoped time queries (Scenario B/C) |
| `idx_readings_recorded_at` | `(recorded_at DESC)` | global time-range scans |
| `idx_reading_values_sensor_type` | `(sensor_type_id)` | per-sensor aggregation (Scenario C) |

Ingestion path: CSV → `staging_airquality` (server-side `COPY`) →
`0002_transform.up.sql` unpivots the 13 sensor columns into `reading_values`.

---

## 3. Internal layering (per service)

Each service uses a clean, layered architecture so the only real difference is the
transport/serialization edge — the persistence logic is equivalent.

```mermaid
flowchart LR
    subgraph rest["REST · C# / ASP.NET Core"]
        R1["ReadingsController<br/>(HTTP + attributes)"] --> R2["ReadingsService"] --> R3["EF Core DbContext<br/>Npgsql"]
    end
    subgraph grpc["gRPC · Go"]
        G1["grpcServer<br/>(generated stubs)"] --> G2["application.ReadingsService"] --> G3["infra.PgxRepository"]
    end
    subgraph gql["GraphQL · Rust"]
        Q1["Actix handler<br/>async-graphql"] --> Q2["Query / Mutation<br/>resolvers (schema.rs)"] --> Q3["DbPool (sqlx)"]
    end

    R3 --> PG[("PostgreSQL")]
    G3 --> PG
    Q3 --> PG
```

All three expose the same five operations:

| Operation | REST | gRPC | GraphQL |
|---|---|---|---|
| List sensor types | `GET /api/sensor-types` | `ListSensorTypes` | `listSensorTypes` |
| List readings | `GET /api/readings` | `ListReadings` | `listReadings` |
| Get reading by id | `GET /api/readings/{id}` | `GetReading` | `getReading` |
| Create reading | `POST /api/readings` | `CreateReading` | `createReading` (mutation) |
| Aggregate | `GET /api/readings/aggregate` | `AggregateReadings` | `aggregateReadings` |

---

## 4. Request data flow by protocol

The same logical request ("give me readings") is shaped differently on the wire.

```mermaid
flowchart TB
    C["Client"]

    C -->|"GET /api/readings?sensors=temperature,rh"| A1
    subgraph restflow["REST"]
        A1["Fixed endpoint + query string"] --> A2["JSON array<br/>(server decides shape)"]
    end

    C -->|"ListReadings(sensorCodes=[...])"| B1
    subgraph grpcflow["gRPC"]
        B1["Typed Protobuf request"] --> B2["Binary Protobuf response<br/>(HTTP/2 framed)"]
    end

    C -->|"query listReadings · select id, measuredAt"| D1
    subgraph gqlflow["GraphQL"]
        D1["Single endpoint,<br/>client-specified field set"] --> D2["JSON with exactly<br/>the requested fields"]
    end
```

Key difference for **Scenario B (selective monitoring)**: REST needs a purpose-built
`sensors=` parameter, gRPC needs `sensor_codes` in the message, but GraphQL lets the
client pick fields *without any server change* — this is the over-fetch-avoidance the
project is built to demonstrate.

---

## 5. Scenario sequence diagrams

### Scenario A — High-Frequency Ingestion (`POST` / `CreateReading` / `createReading`)

```mermaid
sequenceDiagram
    participant C as Client (k6, N VUs)
    participant S as Protocol service
    participant DB as PostgreSQL
    loop each virtual user, as fast as possible
        C->>S: create reading (device, sensor, value, ts)
        S->>S: validate + normalize sensor code
        S->>DB: BEGIN
        S->>DB: INSERT INTO readings ... RETURNING id
        S->>DB: INSERT INTO reading_values ...
        S->>DB: COMMIT
        S-->>C: new id (201 / message / mutation payload)
    end
    Note over C,DB: Measures write throughput + protocol overhead
```

### Scenario B — Selective Monitoring (client wants 2 of many sensors)

```mermaid
sequenceDiagram
    participant C as Client (poor connection)
    participant S as Protocol service
    participant DB as PostgreSQL
    C->>S: list readings, only [temperature, relative_humidity]
    S->>DB: SELECT ... JOIN reading_values JOIN sensor_types<br/>WHERE code = ANY($sensors)
    DB-->>S: rows
    S-->>C: minimal payload (only requested sensors/fields)
    Note over C,S: GraphQL trims fields client-side;<br/>REST/gRPC trim via explicit filter params
```

### Scenario C — Heavy Querying (historical aggregation)

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Protocol service
    participant DB as PostgreSQL
    C->>S: aggregate(sensor, bucketMinutes, from, to)
    S->>DB: SELECT time_bucket, AVG/MIN/MAX/COUNT<br/>GROUP BY bucket ORDER BY bucket
    DB-->>S: aggregate points (server-side reduction)
    S-->>C: compact aggregate series
    Note over DB: Uses recorded_at + sensor_type indexes;<br/>heavy scan over 2004-2005 range
```

---

## 6. Deployment topology (Docker Compose)

```mermaid
flowchart LR
    subgraph compose["docker compose · project-one"]
        direction TB
        PG[("postgres<br/>healthcheck")]
        REST["rest-service-csharp"]
        GRPC["grpc-service-go"]
        GQL["graphql-service-rust"]
        EXP["docker-stats-exporter"]
        PROM["prometheus"]
        GRAF["grafana"]
    end

    REST -. depends_on healthy .-> PG
    GRPC -. depends_on healthy .-> PG
    GQL  -. depends_on healthy .-> PG
    EXP -. depends_on .-> REST & GRPC & GQL
    PROM -. depends_on .-> EXP
    GRAF -. depends_on .-> PROM
```

| Container | Image / build | Host port | Purpose |
|---|---|---|---|
| `postgres` | `postgres:16-alpine` | 5432 | shared datastore |
| `rest-service-csharp` | build | 5000 | REST / JSON / OpenAPI |
| `grpc-service-go` | build | 50051 | gRPC / Protobuf |
| `graphql-service-rust` | build | 8000 | GraphQL / field selection |
| `docker-stats-exporter` | build | 9101 | per-container CPU/RAM → Prometheus |
| `prometheus` | `prom/prometheus` | 9090 | metrics storage |
| `grafana` | `grafana/grafana` | 3000 | dashboards |

---

## Related documents

- [`benchmarks.md`](benchmarks.md) — k6 latency/RPS + docker-stats CPU/RAM results.
- [`protocol-comparison.md`](protocol-comparison.md) — REST vs gRPC vs GraphQL analysis
  and payload-size (JSON vs Protobuf) measurements.
- [`../README.md`](../README.md) — run instructions and quick links.
