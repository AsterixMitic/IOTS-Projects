#!/usr/bin/env bash
# Scenario A — Massive Sensor Ingestion (Kafka)
#
# Simulates 100 / 1000 / 10000 devices publishing at 1 msg/s each and tracks
# throughput (rows persisted/sec) and message loss vs. the expected count.
#
# Usage: scenario_a.sh [acks] [duration_seconds]
#   acks             Kafka producer acks for ingestion (0, 1, or -1 for "all"; default: 1)
#   duration_seconds how long each device-count run lasts (default: 30)

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ../common.sh

ACKS="${1:-1}"
DURATION="${2:-30}"
INTERVAL_MS=1000
DEVICE_COUNTS=(100 1000 10000)

OUT_DIR="$RESULTS_DIR/scenario_a"
mkdir -p "$OUT_DIR"
SUMMARY="$OUT_DIR/kafka_acks${ACKS}_summary.csv"

echo "broker,qos_or_acks,devices,interval_ms,duration_s,expected_msgs,received_db,loss_pct,throughput_msgs_per_s" > "$SUMMARY"

log "Scenario A (Kafka, acks=$ACKS) — recreating ingestion/analytics/storage"
recreate_with_env ingestion BROKER_MODE=kafka KAFKA_ACKS="$ACKS"
recreate_with_env analytics BROKER_MODE=kafka
recreate_with_env storage BROKER_MODE=kafka
wait_for_http "$INGESTION_URL/health"
wait_for_http "$ANALYTICS_URL/health"
wait_for_http "$STORAGE_URL/health"

for devices in "${DEVICE_COUNTS[@]}"; do
  log "=== devices=$devices | duration=${DURATION}s ==="

  #docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -c "TRUNCATE readings CASCADE;" >/dev/null 2>&1
  #sleep 15

  before=$(reading_count)

  stats_file="$OUT_DIR/kafka_acks${ACKS}_devices${devices}_dockerstats.csv"
  capture_docker_stats "$stats_file" "$DURATION" 5 \
    project-two-ingestion project-two-storage project-two-analytics project-two-kafka &
  STATS_PID=$!

  start_simulation "$devices" "$INTERVAL_MS" false $((DURATION * 1000))

  sleep $((DURATION + 5))
  wait "$STATS_PID" 2>/dev/null || true

  after=$(reading_count)
  received=$((after - before))
  expected=$((devices * DURATION * 1000 / INTERVAL_MS))
  loss_pct=$(awk -v e="$expected" -v r="$received" 'BEGIN { if (e>0) printf "%.2f", ((e-r)/e)*100; else print "0.00" }')
  throughput=$(awk -v r="$received" -v d="$DURATION" 'BEGIN { printf "%.2f", r/d }')

  echo "kafka,$ACKS,$devices,$INTERVAL_MS,$DURATION,$expected,$received,$loss_pct,$throughput" >> "$SUMMARY"
  log "devices=$devices expected=$expected received=$received loss=${loss_pct}% throughput=${throughput} msg/s"

  log "Consumer lag (project-two-storage):"
  kafka_consumer_lag "project-two-storage"
  log "Consumer lag (analytics-group):"
  kafka_consumer_lag "analytics-group"
done

log "Scenario A (Kafka, acks=$ACKS) complete. Summary: $SUMMARY"
