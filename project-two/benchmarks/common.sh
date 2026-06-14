#!/usr/bin/env bash
# Shared helpers for benchmark scenario scripts.
# Source this file from benchmarks/{mqtt,kafka}/scenario_*.sh

set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.yml"
RESULTS_DIR="$ROOT_DIR/docs/results"

INGESTION_URL="${INGESTION_URL:-http://localhost:3000}"
ANALYTICS_URL="${ANALYTICS_URL:-http://localhost:3001}"
STORAGE_URL="${STORAGE_URL:-http://localhost:8080}"

DB_CONTAINER="${DB_CONTAINER:-project-two-postgres}"
DB_USER="${POSTGRES_USER:-iot}"
DB_NAME="${POSTGRES_DB:-iot_events}"

KAFKA_CONTAINER="${KAFKA_CONTAINER:-project-two-kafka}"

# Default docker compose network name for this project directory.
# Verify with `docker network ls` and override via COMPOSE_NETWORK if different.
COMPOSE_NETWORK="${COMPOSE_NETWORK:-project-two_default}"

log() {
  echo "[$(date '+%H:%M:%S')] $*"
}

# wait_for_http <url> [retries]
wait_for_http() {
  local url=$1 retries=${2:-30}
  for ((i = 1; i <= retries; i++)); do
    if curl -sf "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for $url" >&2
  return 1
}

# reading_count -> prints total row count in readings table
reading_count() {
  docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -t -A -c "SELECT count(*) FROM readings;" 2>/dev/null \
    | tr -d '[:space:]'
}

storage_health() {
  curl -sf "$STORAGE_URL/health"
}

ingestion_health() {
  curl -sf "$INGESTION_URL/health"
}

analytics_window_stats() {
  curl -sf "$ANALYTICS_URL/window/stats"
}

# start_simulation <deviceCount> <intervalMs> <forceAlert:true|false> <durationMs>
start_simulation() {
  local deviceCount=$1 intervalMs=$2 forceAlert=$3 durationMs=$4
  curl -sf -X POST "$INGESTION_URL/simulate/start" \
    -H 'Content-Type: application/json' \
    -d "{\"deviceCount\":${deviceCount},\"intervalMs\":${intervalMs},\"forceAlert\":${forceAlert},\"durationMs\":${durationMs}}"
  echo
}

stop_simulation() {
  curl -sf -X POST "$INGESTION_URL/simulate/stop" >/dev/null 2>&1 || true
}

# kafka_consumer_lag <group-id>
kafka_consumer_lag() {
  local group=$1
  docker exec "$KAFKA_CONTAINER" /opt/kafka/bin/kafka-consumer-groups.sh \
    --bootstrap-server localhost:9092 --describe --group "$group" 2>/dev/null || true
}

# capture_docker_stats <outfile> <duration_s> <interval_s> <container...>
# Runs in the foreground for duration_s, sampling docker stats every interval_s.
# Intended to be run with `&` by the caller so it overlaps with the workload.
capture_docker_stats() {
  local outfile=$1 duration=$2 interval=$3
  shift 3
  local containers=("$@")
  echo "timestamp,container,cpu_perc,mem_usage,mem_perc,net_io" > "$outfile"
  local elapsed=0
  while [ "$elapsed" -lt "$duration" ]; do
    local ts
    ts=$(date '+%Y-%m-%dT%H:%M:%S')
    docker stats --no-stream --format '{{.Container}},{{.CPUPerc}},{{.MemUsage}},{{.MemPerc}},{{.NetIO}}' "${containers[@]}" 2>/dev/null \
      | while IFS=, read -r c cpu mem memp net; do
          echo "$ts,$c,$cpu,$mem,$memp,$net" >> "$outfile"
        done
    sleep "$interval"
    elapsed=$((elapsed + interval))
  done
}

# recreate_with_env <service> VAR=VAL [VAR2=VAL2 ...]
# Recreates a single compose service with extra/overridden env vars
# (used to switch MQTT_QOS / KAFKA_ACKS / BROKER_MODE between runs).
recreate_with_env() {
  local service=$1
  shift
  env "$@" docker compose -f "$COMPOSE_FILE" up -d --force-recreate "$service"
}
