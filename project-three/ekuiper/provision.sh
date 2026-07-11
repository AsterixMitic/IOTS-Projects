#!/bin/sh
# Provisioning eKuiper streams + pravila preko REST API-ja (Projekat 3, Faza 3).
# Pokreće se kao jednokratni init kontejner posle podizanja eKuiper servisa.
set -e

EKUIPER="${EKUIPER_URL:-http://ekuiper:9081}"
DEFS="$(dirname "$0")"

echo "[provision] Cekam eKuiper REST API na $EKUIPER ..."
until curl -sf "$EKUIPER/streams" >/dev/null 2>&1; do
  sleep 2
done
echo "[provision] eKuiper je podignut."

echo "[provision] Kreiram stream iotStream ..."
curl -s -X DELETE "$EKUIPER/streams/iotStream" >/dev/null 2>&1 || true
curl -s -X POST "$EKUIPER/streams" -H 'Content-Type: application/json' \
  -d @"$DEFS/streams/iotStream.json"
echo

for f in "$DEFS"/rules/*.json; do
  rid=$(basename "$f" .json)
  echo "[provision] Kreiram pravilo $rid ..."
  curl -s -X DELETE "$EKUIPER/rules/$rid" >/dev/null 2>&1 || true
  curl -s -X POST "$EKUIPER/rules" -H 'Content-Type: application/json' -d @"$f"
  echo
done

echo "[provision] Gotovo. Registrovana pravila:"
curl -s "$EKUIPER/rules"
echo
