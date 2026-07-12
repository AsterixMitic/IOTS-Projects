# Web Dashboard (Blazor Server) — Projekat 3

Live dashboard koji prikazuje objedinjenu analitiku IoT toka. **Blazor Server** — server-side MQTT
pretplata (MQTTnet) + push u UI preko SignalR-a (bez CDN-a, bez CORS-a).

## Šta prikazuje

- **Kvalitet vazduha (MaaS):** trenutna ML klasa (Dobar/Umeren/Nezdrav) sa verovatnoćama, obojeno.
- **Tumbling window:** prosek/min/max temperature, broj uzoraka, latencija (iz `iot/analytics`).
- **Trend temperature:** sparkline poslednjih prozora.
- **ML model (MaaS):** tip, verzija, test tačnost (iz `GET /model/info`).
- **eKuiper CEP događaji:** live feed sa `iot/events` + brojači po tipu.
- **Live očitavanja:** poslednja očitavanja sa `iot/stored`.
- **Baner alarma:** temperaturni prag / ML `unhealthy` / kritični CEP.

## Izvori podataka

| Izvor | Kanal |
|---|---|
| `iot/analytics` | MQTT — objedinjeni rezime (prozor + ML klasa) |
| `iot/events` | MQTT — CEP događaji |
| `iot/stored` | MQTT — live očitavanja (osvežavanje throttle-ovano na 1 s) |
| MaaS `/model/info` | HTTP — metapodaci modela |

## Konfiguracija (env)

`MQTT_HOST`, `MQTT_PORT`, `MQTT_STORED_TOPIC`, `MQTT_EVENTS_TOPIC`, `MQTT_ANALYTICS_TOPIC`, `MAAS_URL`.

## Pokretanje

Deo je `docker compose` stack-a (servis `web`, port **8090** → kontejner 8080):
otvoriti <http://localhost:8090>. Za live podatke pokrenuti simulaciju na ingestion servisu.
