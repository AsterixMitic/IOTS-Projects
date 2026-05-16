#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 1 ]; then
  echo "Usage: $0 path/to/AirQualityUCI.csv [--force]"
  exit 1
fi

CSV="$1"
FORCE=false
if [ "${2-}" = "--force" ]; then
  FORCE=true
fi

if [ ! -f "$CSV" ]; then
  echo "CSV file not found: $CSV"
  exit 1
fi

# Basic header check to catch mismatched CSVs
EXPECTED_COLS=17
HEADER_COLS=$(awk -F';' 'NR==1{print NF}' "$CSV") || HEADER_COLS=0
if [ "$HEADER_COLS" -ne "$EXPECTED_COLS" ] && [ "$FORCE" = false ]; then
  echo "Unexpected column count in CSV: $HEADER_COLS (expected $EXPECTED_COLS)."
  echo "If this is expected, re-run with --force to skip this check."
  exit 1
fi

# Copy CSV into container
docker cp "$CSV" project-one-postgres:/tmp/AirQualityUCI.csv

# Ensure schema + staging exist
bash db/run_migrations.sh

# Truncate staging and load CSV (server-side COPY)
docker exec -i project-one-postgres psql -U "${POSTGRES_USER:-iot_user}" -d "${POSTGRES_DB:-iot_project}" -c "TRUNCATE staging_airquality;"

docker exec -i project-one-postgres psql -U "${POSTGRES_USER:-iot_user}" -d "${POSTGRES_DB:-iot_project}" -c "COPY staging_airquality (date_col,time_col,co_gt,pt08_s1_co,nmhc_gt,c6h6_gt,pt08_s2_nmhc,nox_gt,pt08_s3_nox,no2_gt,pt08_s4_no2,pt08_s5_o3,temperature,rh,ah,extra1,extra2) FROM '/tmp/AirQualityUCI.csv' WITH (FORMAT csv, HEADER true, DELIMITER ';', NULL '-200');"

# Run transform
# This performs decimal comma -> dot replacement during numeric casts and populates normalized tables
docker exec -i project-one-postgres psql -U "${POSTGRES_USER:-iot_user}" -d "${POSTGRES_DB:-iot_project}" -q < db/0002_transform.up.sql

echo "Import via COPY complete"
