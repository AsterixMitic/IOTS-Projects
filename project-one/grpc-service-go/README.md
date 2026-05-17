gRPC service (Go)
=================

This service exposes a ReadingsService (proto in `proto/iot/v1/readings.proto`) and connects to the project's PostgreSQL database.

Quick start (with docker-compose):

1. Start the database and REST service (if not running):

   ```bash
   cd project-one
   docker compose up -d postgres rest-service-csharp
   ```

2. Build and run the gRPC service with compose (this repo):

   ```bash
   docker compose up --build grpc-service-go
   ```

3. The gRPC endpoint is available on port `50051` on the host.

Client generation
-----------------
- Proto file: `proto/iot/v1/readings.proto`
- Generated Go stubs are under `gen/proto/iot/v1`.

Development notes
-----------------
- The service uses pgx (pgxpool) and a small clean-architecture layout (internal/domain, internal/infra, internal/application).
- The service expects the environment variable `DATABASE_URL` (falls back to values from `.env` via compose) and `GRPC_PORT`.

