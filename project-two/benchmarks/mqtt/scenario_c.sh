#!/usr/bin/env bash
# Scenario C — Burst Event Load (MQTT)
#
# Runs a low baseline rate (~50 msg/s), then a sudden burst (~5000 msg/s) for
# a short period, then returns to baseline. Tracks persisted-row growth over
# time to observe backlog formation, backpressure, and recovery time.
#
# Usage: scenario_c.sh [qos]
#   qos MQTT QoS level for ingestion + analytics + storage (default: 1)

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ../common.sh

QOS="${1:-1}"

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
TIMELINE="$OUT_DIR/mqtt_qos${QOS}_timeline.csv"
STATS_FILE="$OUT_DIR/mqtt_qos${QOS}_dockerstats.csv"

log "Scenario C (MQTT, QoS=$QOS) — recreating ingestion/analytics/storage"
recreate_with_env ingestion BROKER_MODE=mqtt MQTT_QOS="$QOS"
recreate_with_env analytics BROKER_MODE=mqtt MQTT_QOS="$QOS"
recreate_with_env storage BROKER_MODE=mqtt MQTT_QOS="$QOS"
wait_for_http "$INGESTION_URL/health"
wait_for_http "$ANALYTICS_URL/health"
wait_for_http "$STORAGE_URL/health"

echo "elapsed_s,phase,readings_total" > "$TIMELINE"
before=$(reading_count)

TOTAL_SECONDS=$((BASELINE_SECONDS + BURST_SECONDS + RECOVERY_SECONDS))
capture_docker_stats "$STATS_FILE" "$TOTAL_SECONDS" "$SAMPLE_INTERVAL" \
  project-two-ingestion project-two-storage project-two-analytics project-two-mosquitto &
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

log "Recovery: monitoring backlog drain for ${RECOVERY_SECONDS}s"
for ((t = 0; t < RECOVERY_SECONDS; t += SAMPLE_INTERVAL)); do
  count=$(reading_count)
  echo "$((BASELINE_SECONDS + BURST_SECONDS + t)),recovery,$((count - before))" >> "$TIMELINE"
  sleep "$SAMPLE_INTERVAL"
done

wait "$STATS_PID" 2>/dev/null || true

log "Scenario C (MQTT, QoS=$QOS) complete. Timeline: $TIMELINE | Docker stats: $STATS_FILE"
