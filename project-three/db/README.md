# Database bootstrap

This folder seeds the project-two PostgreSQL database with the same Air Quality dataset used in project one.

## Files

- `0001_schema_and_staging.up.sql` creates the base schema and staging table.
- `0002_transform.up.sql` normalizes the staged CSV rows into queryable tables.
- `dataset/AirQualityUCI.csv` is the dataset copy used for imports.

## Run the import

1. Start PostgreSQL:

```bash
docker compose up -d postgres
```

2. Apply migrations:

```bash
bash db/run_migrations.sh
```

3. Load the dataset:

```bash
bash db/import_via_copy.sh
```

The import script defaults to the bundled `dataset/AirQualityUCI.csv`, so the project can be bootstrapped without extra arguments.
