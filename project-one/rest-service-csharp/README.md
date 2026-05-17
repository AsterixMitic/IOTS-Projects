# REST service (C# / ASP.NET Core)

This service exposes IoT data from the shared PostgreSQL database used across all protocols.
It uses a controller-based ASP.NET Core API with EF Core (Npgsql provider).

## Run locally

```bash
cd project-one/rest-service-csharp/src/RestService
dotnet run
```

By default, the API expects PostgreSQL at:

`Host=localhost;Port=5432;Database=iot_project;Username=iot_user;Password=iot_password`

Override with environment variable:

`ConnectionStrings__Postgres=Host=...;Port=...;Database=...;Username=...;Password=...`

## OpenAPI

- OpenAPI JSON: `http://localhost:5000/openapi/v1.json` (when running in Docker Compose)

## Endpoints

- `GET /api/sensor-types`
- `GET /api/readings?deviceId=1&from=2004-03-10T00:00:00Z&to=2004-03-11T00:00:00Z&sensors=temperature,relative_humidity&limit=100&offset=0`
- `GET /api/readings/{id}`
- `POST /api/readings`
- `GET /api/readings/aggregate?sensorCode=temperature&bucketMinutes=60&deviceId=1`

### POST /api/readings example

```json
{
  "deviceId": 1,
  "recordedAt": "2026-05-17T00:00:00Z",
  "notes": "manual ingestion sample",
  "values": {
    "temperature": 21.5,
    "relative_humidity": 47.2,
    "co_gt": 0.8
  }
}
```

## Postman scripts

Import these files:

- `project-one/rest-service-csharp/postman/RestService.postman_collection.json`
- `project-one/rest-service-csharp/postman/RestService.local.postman_environment.json`

Run requests in collection order (`1` through `7`) so variables (`readingId`, `createdReadingId`) are populated automatically.

Note: request `5) Create Reading` inserts a new row into the database for testing.
