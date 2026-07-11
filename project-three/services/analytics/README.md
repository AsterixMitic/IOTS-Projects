# Analytics Service (Projekat 3)

Node.js mikroservis koji objedinjuje tri izvora analize IoT toka i izlaže rezultate preko REST-a
i preko MQTT topica `iot/analytics`.

## Šta radi

1. **`iot/stored`** (perzistirana očitavanja) → **tumbling window** (10 s): prosek/min/max
   temperature, latencija, alarm ako prosek pređe `ALERT_THRESHOLD`.
2. **`iot/events`** (eKuiper CEP događaji) → bafer poslednjih događaja + brojači po tipu;
   kritični događaji (npr. `POLLUTION_SPIKE`) idu u agregirane alarme.
3. **MaaS REST `/predict`** → po zatvaranju svakog prozora šalje **prosečno očitavanje** i dobija
   klasu kvaliteta vazduha (`good/moderate/unhealthy`). Robustno: timeout + graceful degradacija
   (ako je MaaS nedostupan, predikcija je `null`, servis nastavlja da radi).

Po zatvaranju prozora publikuje **objedinjeni rezime** (`ANALYTICS_SUMMARY`) na `iot/analytics`
— koristi ga web dashboard (Faza 5).

## REST endpointi (port 3001)

| Ruta | Opis |
|---|---|
| `GET /health` | status + window/events/predictions/maas snapshot |
| `GET /config` | aktivna konfiguracija (topici, MaaS URL) |
| `GET /window/stats` | statistika tumbling window-a |
| `GET /events` | poslednji eKuiper CEP događaji + brojači po tipu |
| `GET /predictions` | poslednje MaaS predikcije + status MaaS-a |
| `GET /alerts` | objedinjeni alarmi (prag + CEP + ML `unhealthy`) |

## Moduli

| Fajl | Uloga |
|---|---|
| `src/index.js` | orkestracija + HTTP API |
| `src/mqtt-bus.js` | MQTT klijent: multi-topic subscribe + publish |
| `src/maas-client.js` | HTTP klijent za MaaS (timeout, graceful degradacija) |
| `src/tumbling-window.js` | prozor: agregacija temperature + prosečno očitavanje za MaaS |

## Konfiguracija (env)

`MQTT_STORED_TOPIC`, `MQTT_EVENTS_TOPIC`, `MQTT_ANALYTICS_TOPIC`, `MQTT_QOS`,
`WINDOW_SECONDS`, `ALERT_THRESHOLD`, `MAAS_URL`, `MAAS_ENABLED`, `MAAS_TIMEOUT_MS`.

Projekat 3 je MQTT-native (Kafka putanja iz P2 je uklonjena iz ovog servisa).
