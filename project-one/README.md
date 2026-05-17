# Project one - Microservices Communication

This project demonstrates implementation and comparison of different microservice communication approaches using modern backend technologies and observability tools.

The goal of the project is to explore how different communication protocols behave in distributed systems environments, as well as to analyze their performance, scalability, developer experience, and monitoring capabilities.

---

## Database bootstrap

The project starts with a single PostgreSQL container defined in `docker-compose.yml`.

```bash
docker compose up -d postgres
```

Schema setup is migration-based. After starting PostgreSQL, run migrations and then load the UCI Air Quality dataset.

```bash
# apply migrations and import CSV
cd project-one
bash db/run_migrations.sh
bash db/import_via_copy.sh path/to/AirQualityUCI.csv
```

---

# Technologies

| Communication Type | Technology Stack |
|---|---|
| REST API | C# / ASP.NET Core |
| gRPC Service | Go |
| GraphQL API | Rust |
| Monitoring | Prometheus |
| Visualization | Grafana |
| Containerization | Docker & Docker Compose |

---

# Project Goals

- Implement equivalent microservices using different communication technologies
- Compare REST, gRPC, and GraphQL approaches
- Analyze request performance and latency
- Monitor services using Prometheus
- Visualize metrics and system health using Grafana
- Explore distributed systems architecture concepts

---

# Architecture Overview

Target architecture consists of three independent microservices:

```text
Client
   │
   ├── REST API (C# / ASP.NET Core)
   │
   ├── gRPC Service (Go)
   │
   └── GraphQL API (Rust)
```

Each service exposes similar business functionality while using a different communication protocol and technology stack.
At the current stage, the REST service is implemented first and wired to PostgreSQL.

---

# Project Structure

```text
project-one/
│
├── rest-service-csharp/
│   ├── src/
│   ├── Dockerfile
│   └── README.md
│
├── grpc-service-go/
│   ├── cmd/
│   ├── proto/
│   ├── Dockerfile
│   └── README.md
│
├── graphql-service-rust/
│   ├── src/
│   ├── Cargo.toml
│   ├── Dockerfile
│   └── README.md
│
├── monitoring/
│   ├── prometheus/
│   │   └── prometheus.yml
│   │
│   └── grafana/
│       └── dashboards/
│
├── docs/
│   ├── architecture.md
│   ├── benchmarks.md
│   └── protocol-comparison.md
│
└── docker-compose.yml
```

---

# Communication Technologies

## REST

Implemented using ASP.NET Core in C#.
Current implementation uses controller-based endpoints with EF Core over PostgreSQL.

### Characteristics

- HTTP-based communication
- Human-readable JSON payloads
- Widely adopted and easy to integrate
- Suitable for standard CRUD operations

---

## gRPC

Implemented using Go.

### Characteristics

- High-performance binary protocol
- Uses Protocol Buffers
- Supports streaming
- Strongly typed contracts
- Efficient for inter-service communication

---

## GraphQL

Implemented using Rust.

### Characteristics

- Flexible querying
- Clients request only required data
- Single endpoint architecture
- Reduces over-fetching and under-fetching

---

# Monitoring & Observability

## Prometheus (planned)

Prometheus will be used for:

- Request counting
- Response time metrics
- Error tracking
- Service health monitoring
- Resource usage monitoring

Example metrics:

- HTTP request duration
- Requests per second
- Error rates
- Memory usage
- CPU usage

---

## Grafana (planned)

Grafana will be used for visualization and dashboard creation.

Dashboards may include:

- Service latency comparison
- Request throughput
- Error distribution
- Resource consumption
- Real-time monitoring panels

---

# Running the Project

## Prerequisites

Required software:

- Docker
- Docker Compose
- .NET SDK
- Go
- Rust
- Git

---

# Running with Docker Compose

From the `project-one/` directory:

```bash
docker compose up --build
```

This currently starts:

- PostgreSQL database
- REST service (C# / ASP.NET Core)

### REST testing quick links

- OpenAPI JSON: `http://localhost:5000/openapi/v1.json`
- Postman assets: `rest-service-csharp/postman/`

---

# Active Service Ports

| Service | Port |
|---|---|
| PostgreSQL | 5432 |
| REST API | 5000 |

---

# Current runtime note

The current `docker-compose.yml` starts PostgreSQL and the REST service. Prometheus and Grafana sections above describe the planned observability phase and are not active yet.

---

# Benchmarking & Analysis

The project may include benchmarking and protocol comparison based on:

- Response latency
- Throughput
- Payload size
- Serialization overhead
- Scalability
- Developer experience
- Ease of integration

Results and analysis can be found inside:

```text
docs/benchmarks.md
```

---

# Future Improvements

Possible future extensions:

- Kubernetes deployment
- API Gateway integration
- Authentication & authorization
- Distributed tracing
- CI/CD pipelines
- Service discovery
- Load balancing

---

# Author

Aleksandar Mitic  
Faculty of Electronic Engineering, University of Nis