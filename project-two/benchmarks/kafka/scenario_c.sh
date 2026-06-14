#!/usr/bin/env bash
# Scenario C — Burst Event Load (Kafka)
#
# Runs a low baseline rate (~50 msg/s), then a sudden burst (~5000 msg/s) for
# a short period, then returns to baseline. Tracks persisted-row growth over
# time to observe backlog formation, backpressure, and recovery time, plus
# consumer lag at the end of each phase.
#
# Usage: scenario_c.sh [acks]
#   acks Kafka producer acks for ingestion (0, 1, or -1 for "all"; default: 1)

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ../common.sh

ACKS="${1:-1}"

BASELINE_DEVICES=50      # 50 devices * 1 msg/s  = ~50 msg/s
BASELINE_INTERVAL_MS=1000
BASELINE_SECONDS=15

BURST_DEVICES=500        # 500 devices * 10 msg/s = ~5000 msg/s
BURST_INTERVAL_MS=100
BURST_SECONDS=10

RECOVERY_SECONDS=30
SAMPLE_INTERVAL=2

OUT_DIR="$RESULTS_DIR/scenario_c"
mkdir -p "$OUT_DIR"
TIMELINE="$OUT_DIR/kafka_acks${ACKS}_timeline.csv"
STATS_FILE="$OUT_DIR/kafka_acks${ACKS}_dockerstats.csv"

log "Scenario C (Kafka, acks=$ACKS) — recreating ingestion/analytics/storage"
recreate_with_env ingestion BROKER_MODE=kafka KAFKA_ACKS="$ACKS"
recreate_with_env analytics BROKER_MODE=kafka
recreate_with_env storage BROKER_MODE=kafka
wait_for_http "$INGESTION_URL/health"
wait_for_http "$ANALYTICS_URL/health"
wait_for_http "$STORAGE_URL/health"

echo "elapsed_s,phase,readings_total" > "$TIMELINE"
before=$(reading_count)

TOTAL_SECONDS=$((BASELINE_SECONDS + BURST_SECONDS + RECOVERY_SECONDS))
capture_docker_stats "$STATS_FILE" "$TOTAL_SECONDS" "$SAMPLE_INTERVAL" \
  project-two-ingestion project-two-storage project-two-analytics project-two-kafka &
STATS_PID=$!

log "Baseline: $BASELINE_DEVICES devices @ ${BASELINE_INTERVAL_MS}ms (~50 msg/s) for ${BASELINE_SECONDS}s"
start_simulation "$BASELINE_DEVICES" "$BASELINE_INTERVAL_MS" false $((BASELINE_SECONDS * 1000))
for ((t = 0; t < BASELINE_SECONDS; t += SAMPLE_INTERVAL)); do
  count=$(reading_count)
  echo "$t,baseline,$((count - before))" >> "$TIMELINE"
  sleep "$SAMPLE_INTERVAL"
done
stop_simulation

log "Burst: $BURST_DEVICES devices @ ${BURST_INTERVAL_MS}ms (~5000 msg/s) for ${BURST_SECONDS}s"
start_simulation "$BURST_DEVICES" "$BURST_INTERVAL_MS" false $((BURST_SECONDS * 1000))
for ((t = 0; t < BURST_SECONDS; t += 1)); do
  count=$(reading_count)
  echo "$((BASELINE_SECONDS + t)),burst,$((count - before))" >> "$TIMELINE"
  sleep 1
done
stop_simulation

log "Consumer lag right after burst:"
kafka_consumer_lag "project-two-storage"
kafka_consumer_lag "analytics-group"

log "Recovery: monitoring backlog drain for ${RECOVERY_SECONDS}s"
for ((t = 0; t < RECOVERY_SECONDS; t += SAMPLE_INTERVAL)); do
  count=$(reading_count)
  echo "$((BASELINE_SECONDS + BURST_SECONDS + t)),recovery,$((count - before))" >> "$TIMELINE"
  sleep "$SAMPLE_INTERVAL"
done

wait "$STATS_PID" 2>/dev/null || true

log "Consumer lag after recovery:"
kafka_consumer_lag "project-two-storage"
kafka_consumer_lag "analytics-group"

log "Scenario C (Kafka, acks=$ACKS) complete. Timeline: $TIMELINE | Docker stats: $STATS_FILE"
