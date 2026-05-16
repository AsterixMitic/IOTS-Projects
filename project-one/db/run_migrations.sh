#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

# Load environment from project .env if present
if [ -f ../.env ]; then
  export $(grep -v '^#' ../.env | xargs)
fi

MIGRATIONS=(0001_schema_and_staging.up.sql 0002_transform.up.sql)
for m in "${MIGRATIONS[@]}"; do
  echo "Applying $m"
  docker exec -i project-one-postgres psql -U "${POSTGRES_USER:-iot_user}" -d "${POSTGRES_DB:-iot_project}" -q < "$m"
done

echo "Migrations applied"
