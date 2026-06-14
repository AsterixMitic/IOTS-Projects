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
KAFKA_TOPIC="${KAFKA_TOPIC:-iot.readings}"

echo "broker,qos_or_acks,devices,interval_ms,duration_s,expected_msgs,broker_received,broker_loss_pct,broker_throughput_msgs_per_s,persisted_db,persisted_throughput_msgs_per_s,storage_lag_end" > "$SUMMARY"

log "Scenario A (Kafka, acks=$ACKS) — recreating ingestion/analytics/storage"
recreate_with_env ingestion BROKER_MODE=kafka KAFKA_ACKS="$ACKS"
recreate_with_env analytics BROKER_MODE=kafka
recreate_with_env storage BROKER_MODE=kafka
wait_for_http "$INGESTION_URL/health"
wait_for_http "$ANALYTICS_URL/health"
wait_for_http "$STORAGE_URL/health"

for devices in "${DEVICE_COUNTS[@]}"; do
  log "=== devices=$devices | duration=${DURATION}s ==="

  log "Draining any leftover backlog before this tier..."
  wait_for_storage_idle

  before_db=$(reading_count)
  before_offset=$(kafka_topic_total_offset "$KAFKA_TOPIC")

  stats_file="$OUT_DIR/kafka_acks${ACKS}_devices${devices}_dockerstats.csv"
  capture_docker_stats "$stats_file" "$DURATION" 5 \
    project-two-ingestion project-two-storage project-two-analytics project-two-kafka &
  STATS_PID=$!

  start_simulation "$devices" "$INTERVAL_MS" false $((DURATION * 1000))

  sleep $((DURATION + 5))
  wait "$STATS_PID" 2>/dev/null || true

  after_db=$(reading_count)
  after_offset=$(kafka_topic_total_offset "$KAFKA_TOPIC")

  expected=$((devices * DURATION * 1000 / INTERVAL_MS))

  # Broker-level: how many messages Kafka actually appended to the topic
  # during this run (acks=all => should be ~0 loss regardless of storage speed).
  broker_received=$((after_offset - before_offset))
  broker_loss_pct=$(awk -v e="$expected" -v r="$broker_received" 'BEGIN { if (e>0) printf "%.2f", ((e-r)/e)*100; else print "0.00" }')
  broker_throughput=$(awk -v r="$broker_received" -v d="$DURATION" 'BEGIN { printf "%.2f", r/d }')

  # Storage-level: how many of those messages made it into Postgres within
  # duration+5s. The gap vs broker_received reflects storage/DB I/O being the
  # bottleneck (see assignment note on batching/disabling writes for A and C),
  # not message loss on the broker.
  persisted_db=$((after_db - before_db))
  persisted_throughput=$(awk -v r="$persisted_db" -v d="$DURATION" 'BEGIN { printf "%.2f", r/d }')
  storage_lag_end=$(kafka_consumer_lag_total "project-two-storage")

  echo "kafka,$ACKS,$devices,$INTERVAL_MS,$DURATION,$expected,$broker_received,$broker_loss_pct,$broker_throughput,$persisted_db,$persisted_throughput,$storage_lag_end" >> "$SUMMARY"
  log "devices=$devices expected=$expected broker_received=$broker_received broker_loss=${broker_loss_pct}% broker_throughput=${broker_throughput} msg/s | persisted_db=$persisted_db persisted_throughput=${persisted_throughput} msg/s storage_lag=$storage_lag_end"

  log "Consumer lag (project-two-storage):"
  kafka_consumer_lag "project-two-storage"
  log "Consumer lag (analytics-group):"
  kafka_consumer_lag "analytics-group"
done

log "Scenario A (Kafka, acks=$ACKS) complete. Summary: $SUMMARY"
