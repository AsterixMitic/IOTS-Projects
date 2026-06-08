#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if [ -f ../.env ]; then
  set -a
  . ../.env
  set +a
fi

MIGRATIONS=(0001_schema_and_staging.up.sql 0002_transform.up.sql)
for migration in "${MIGRATIONS[@]}"; do
  echo "Applying $migration"
  docker exec -i project-two-postgres psql -U "${POSTGRES_USER:-iot}" -d "${POSTGRES_DB:-iot_events}" -q < "$migration"
done

echo "Migrations applied"
