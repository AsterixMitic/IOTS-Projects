# Database bootstrap

The PostgreSQL container is defined in `../docker-compose.yml`.

## Start the database

```bash
docker compose up -d postgres
```

## Connect

```bash
docker exec -it project-one-postgres psql -U iot_user -d iot_project
```

## Import the dataset

Use the UCI Air Quality CSV (`AirQualityUCI.csv`) and map `Date` + `Time` into `readings.recorded_at`.
Store the per-row measurements in `reading_values` using the `sensor_types` catalog created by the init script.

A repeatable, fast import flow uses a staging table + server-side COPY + SQL transform. Migrations are provided in `db/0001_schema_and_staging.up.sql` and `db/0002_transform.up.sql`.

To run the import against a running container:

```bash
# from project-one/ directory
bash db/import_via_copy.sh path/to/AirQualityUCI.csv
```

The script will validate the CSV header (17 columns) and copy the file into the container, load into `staging_airquality`, and run the transform. If your CSV has extra trailing columns, the staging table tolerates them; to skip the header column-count check, pass `--force` as a second argument.

The current import load counts: 9,357 readings and 105,397 measurement values.


## Connecting from the host

If you have the `psql` client installed locally, connect directly to the mapped port:

```bash
PGPASSWORD="iot_password" psql -h localhost -p 5432 -U iot_user -d iot_project
```

On Windows PowerShell (use double quotes and $env):

```powershell
$env:PGPASSWORD = "iot_password"
psql -h localhost -p 5432 -U iot_user -d iot_project
```

If you don't have a local `psql` client, use a temporary Postgres client container:

```bash
docker run --rm -it --network host postgres:16-alpine psql -h host.docker.internal -U iot_user -d iot_project
```

(If `--network host` is not available on Docker Desktop for Windows, use `-p` mapping and connect to `localhost` instead.)

## Migrations & faster imports

A faster, repeatable import flow uses a staging table + COPY + SQL transform (bulk-friendly).
Migrations are provided in `db/0001_schema_and_staging.up.sql` and `db/0002_transform.up.sql`.

Run all migrations (creates staging table if missing):

```bash
bash db/run_migrations.sh
```

To import a local CSV file using server-side COPY and then transform into normalized tables:

```bash
bash db/import_via_copy.sh path/to/AirQualityUCI.csv
```

This script will:
- copy the CSV into the running container
- ensure the schema + staging table exist
- COPY the CSV into `staging_airquality`
- run the transform migration to populate `readings` and `reading_values`

Note: The staging table expects the UCI column order. The script treats `-200` as NULL.

