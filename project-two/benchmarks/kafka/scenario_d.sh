#!/usr/bin/env bash
# Scenario D — Real-Time Alerting (Kafka)
#
# Forces all simulated readings to have temperature > 50C so every 10s
# tumbling window in the analytics service raises an ALERT. Captures the
# analytics service logs, which include the avg_latency (ms) between the
# moment the ingestion service generated a reading and the moment the
# analytics window processed it.
#
# Usage: scenario_d.sh [acks]
#   acks Kafka producer acks for ingestion (0, 1, or -1 for "all"; default: 1)

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ../common.sh

ACKS="${1:-1}"
DEVICES=5
INTERVAL_MS=1000
DURATION_SECONDS=25  # spans at least two 10s windows

OUT_DIR="$RESULTS_DIR/scenario_d"
mkdir -p "$OUT_DIR"
LOGFILE="$OUT_DIR/kafka_acks${ACKS}_alerts.log"

log "Scenario D (Kafka, acks=$ACKS) — recreating ingestion/analytics/storage"
recreate_with_env ingestion BROKER_MODE=kafka KAFKA_ACKS="$ACKS"
recreate_with_env analytics BROKER_MODE=kafka
recreate_with_env storage BROKER_MODE=kafka
wait_for_http "$INGESTION_URL/health"
wait_for_http "$ANALYTICS_URL/health"
wait_for_http "$STORAGE_URL/health"

START_TS=$(date '+%Y-%m-%dT%H:%M:%S')
log "Starting forced-alert simulation: $DEVICES devices for ${DURATION_SECONDS}s"
start_simulation "$DEVICES" "$INTERVAL_MS" true $((DURATION_SECONDS * 1000))

sleep $((DURATION_SECONDS + 5))
stop_simulation

log "Collecting analytics logs since $START_TS"
docker logs project-two-analytics --since "$START_TS" 2>&1 | grep -E "ALERT|window #" > "$LOGFILE" || true

cat "$LOGFILE"
log "Scenario D (Kafka, acks=$ACKS) complete. Log: $LOGFILE"
