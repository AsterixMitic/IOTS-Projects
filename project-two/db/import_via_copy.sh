#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

CSV="${1:-dataset/AirQualityUCI.csv}"
FORCE=false
if [ "${2-}" = "--force" ]; then
  FORCE=true
fi

if [ ! -f "$CSV" ]; then
  echo "CSV file not found: $CSV"
  exit 1
fi

EXPECTED_COLS=17
HEADER_COLS=$(awk -F';' 'NR==1{print NF}' "$CSV") || HEADER_COLS=0
if [ "$HEADER_COLS" -ne "$EXPECTED_COLS" ] && [ "$FORCE" = false ]; then
  echo "Unexpected column count in CSV: $HEADER_COLS (expected $EXPECTED_COLS)."
  echo "If this is expected, re-run with --force to skip this check."
  exit 1
fi

if [ -f ../.env ]; then
  set -a
  . ../.env
  set +a
fi

docker cp "$CSV" project-two-postgres:/tmp/AirQualityUCI.csv
bash run_migrations.sh

docker exec -i project-two-postgres psql -U "${POSTGRES_USER:-iot}" -d "${POSTGRES_DB:-iot_events}" -c "TRUNCATE staging_airquality;"
docker exec -i project-two-postgres psql -U "${POSTGRES_USER:-iot}" -d "${POSTGRES_DB:-iot_events}" -c "COPY staging_airquality (date_col,time_col,co_gt,pt08_s1_co,nmhc_gt,c6h6_gt,pt08_s2_nmhc,nox_gt,pt08_s3_nox,no2_gt,pt08_s4_no2,pt08_s5_o3,temperature,rh,ah,extra1,extra2) FROM '/tmp/AirQualityUCI.csv' WITH (FORMAT csv, HEADER true, DELIMITER ';', NULL '-200');"
docker exec -i project-two-postgres psql -U "${POSTGRES_USER:-iot}" -d "${POSTGRES_DB:-iot_events}" -q < 0002_transform.up.sql

echo "Import via COPY complete"
