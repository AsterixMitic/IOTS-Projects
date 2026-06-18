#!/usr/bin/env bash
# Scenario D — Real-Time Alerting (MQTT)
#
# Forces all simulated readings to have temperature > 50C so every 10s
# tumbling window in the analytics service raises an ALERT. Captures the
# analytics service logs, which include the avg_latency (ms) between the
# moment the ingestion service generated a reading and the moment the
# analytics window processed it.
#
# Usage: scenario_d.sh [qos]
#   qos MQTT QoS level for ingestion + analytics + storage (default: 1)

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ../common.sh

QOS="${1:-1}"
DEVICES=5
INTERVAL_MS=1000
DURATION_SECONDS=25  # spans at least two 10s windows

OUT_DIR="$RESULTS_DIR/scenario_d"
mkdir -p "$OUT_DIR"
LOGFILE="$OUT_DIR/mqtt_qos${QOS}_alerts.log"

log "Scenario D (MQTT, QoS=$QOS) — recreating ingestion/analytics/storage"
recreate_with_env ingestion BROKER_MODE=mqtt MQTT_QOS="$QOS"
recreate_with_env analytics BROKER_MODE=mqtt MQTT_QOS="$QOS"
recreate_with_env storage BROKER_MODE=mqtt MQTT_QOS="$QOS"
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
log "Scenario D (MQTT, QoS=$QOS) complete. Log: $LOGFILE"
