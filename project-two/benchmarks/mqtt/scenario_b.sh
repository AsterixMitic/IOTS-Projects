#!/usr/bin/env bash
# Scenario B — Edge Connectivity Failures (MQTT)
#
# Starts a small steady simulation, disconnects the ingestion container from
# the docker network for 30s (simulating an edge outage), reconnects it, and
# tracks how the persisted-row count recovers afterwards. With MQTT_QOS >= 1
# and a persistent session (clean=false on the analytics consumer), messages
# queued by the broker while a subscriber is offline should be redelivered.
#
# Usage: scenario_b.sh [qos] [devices]
#   qos     MQTT QoS level for ingestion + analytics + storage (default: 1)
#   devices number of simulated devices (default: 20)

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ../common.sh

QOS="${1:-1}"
DEVICES="${2:-20}"
INTERVAL_MS=1000
DISCONNECT_AT=15
DISCONNECT_SECONDS=30
TOTAL_SECONDS=90
SAMPLE_INTERVAL=5

OUT_DIR="$RESULTS_DIR/scenario_b"
mkdir -p "$OUT_DIR"
TIMELINE="$OUT_DIR/mqtt_qos${QOS}_timeline.csv"

log "Scenario B (MQTT, QoS=$QOS) — recreating ingestion/analytics/storage"
recreate_with_env ingestion BROKER_MODE=mqtt MQTT_QOS="$QOS"
recreate_with_env analytics BROKER_MODE=mqtt MQTT_QOS="$QOS"
recreate_with_env storage BROKER_MODE=mqtt MQTT_QOS="$QOS"
wait_for_http "$INGESTION_URL/health"
wait_for_http "$ANALYTICS_URL/health"
wait_for_http "$STORAGE_URL/health"

echo "elapsed_s,phase,readings_total" > "$TIMELINE"
before=$(reading_count)

log "Starting simulation: $DEVICES devices, ${TOTAL_SECONDS}s, 1 msg/s/device"
start_simulation "$DEVICES" "$INTERVAL_MS" false $((TOTAL_SECONDS * 1000))

reconnect_at=$((DISCONNECT_AT + DISCONNECT_SECONDS))
for ((t = 0; t <= TOTAL_SECONDS; t += SAMPLE_INTERVAL)); do
  phase="normal"

  if [ "$t" -eq "$DISCONNECT_AT" ]; then
    log "Disconnecting project-two-ingestion from network '$COMPOSE_NETWORK' for ${DISCONNECT_SECONDS}s"
    docker network disconnect "$COMPOSE_NETWORK" project-two-ingestion 2>/dev/null \
      || log "WARNING: disconnect failed — check COMPOSE_NETWORK (try: docker network ls)"
  fi

  if [ "$t" -ge "$DISCONNECT_AT" ] && [ "$t" -lt "$reconnect_at" ]; then
    phase="disconnected"
  fi

  if [ "$t" -eq "$reconnect_at" ]; then
    log "Reconnecting project-two-ingestion to network '$COMPOSE_NETWORK'"
    docker network connect "$COMPOSE_NETWORK" project-two-ingestion 2>/dev/null \
      || log "WARNING: reconnect failed — check COMPOSE_NETWORK"
    phase="recovering"
  fi

  if [ "$t" -gt "$reconnect_at" ]; then
    phase="recovering"
  fi

  count=$(reading_count)
  echo "$t,$phase,$((count - before))" >> "$TIMELINE"
  sleep "$SAMPLE_INTERVAL"
done

stop_simulation

log "Scenario B (MQTT, QoS=$QOS) complete. Timeline: $TIMELINE"
log "Final storage health: $(storage_health)"
