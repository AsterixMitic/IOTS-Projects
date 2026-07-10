# Projekat 3 — IoT analitika sa eKuiper (CEP) i MaaS (ML)

Nadogradnja **Analytics** mikroservisa iz [Projekta 2](../project-two) tako da za analizu
podataka koristi:

- **eKuiper** — streaming / CEP (Complex Event Processing) servis preko MQTT brokera, i
- **MaaS** — Model-as-a-Service mikroservis (Python + FastAPI + scikit-learn) preko REST endpointa.

Ceo sistem je kontejnerizovan (Docker Compose) i koristi isti IoT dataset (Air Quality UCI) i
model podataka kao Projekti 1 i 2. Detaljan plan realizacije po fazama je u [PLAN.md](PLAN.md).

> **Status:** Faza 0 (scaffolding) — samostalan MQTT stack sa servisima iz P2. Naredne faze
> (Storage re-publish → MaaS → eKuiper → Analytics++ → Blazor web) su opisane u [PLAN.md](PLAN.md).

## Arhitektura (ciljna)

```
Ingestion ─iot/readings─► Storage (.NET) ─► Postgres
                                └─ re-publish ─► iot/stored ─┬─► eKuiper (CEP) ─► iot/events ─┐
                                                             └─► Analytics (Node.js) ◄────────┘
                                                                      ├─ tumbling window
                                                                      ├─ MaaS REST /predict
                                                                      └─► Blazor web dashboard
```

MQTT topici: `iot/readings` (sirovo) → `iot/stored` (perzistirano) → `iot/events` (CEP događaji).

## Mikroservisi

| Servis | Tehnologija | Uloga | Status |
|---|---|---|---|
| Ingestion | Node.js | Simulira uređaje i publikuje očitavanja na MQTT | ✅ (iz P2) |
| Storage | .NET | Konzumira očitavanja, batch upis u PostgreSQL (+ re-publish na `iot/stored`) | ✅ upis / ⏳ re-publish |
| Analytics | Node.js | Tumbling window + (uskoro) eKuiper događaji + MaaS predikcije | ✅ window / ⏳ CEP+ML |
| eKuiper | lfedge/ekuiper | CEP pravila nad tokom → događaji na `iot/events` | ⏳ Faza 3 |
| MaaS | Python / FastAPI | Klasifikacija kvaliteta vazduha (scikit-learn), REST `/predict` | ⏳ Faza 2 |
| Web | Blazor (.NET) | Live dashboard: očitavanja, događaji, ML klasa | ⏳ Faza 5 |

## Pokretanje

```bash
cp .env.example .env          # lokalna konfiguracija (nije u gitu)
docker compose up -d --build  # postgres, mosquitto, ingestion, storage, analytics
```

Postgres se pri prvom podizanju automatski inicijalizuje šemom i seed-om
(`db/0001_schema_and_staging.up.sql`). Za učitavanje istorijskog dataseta:

```bash
bash db/import_via_copy.sh
```

### Pokretanje simulacije

```bash
curl -X POST http://localhost:3000/simulate/start \
  -H 'Content-Type: application/json' \
  -d '{ "deviceCount": 10, "intervalMs": 1000 }'
```

### Endpointi

| Servis | URL | Rute |
|---|---|---|
| ingestion | http://localhost:3000 | `/health`, `/config`, `POST /simulate/start`, `POST /simulate/stop` |
| analytics | http://localhost:3001 | `/health`, `/config`, `/window/stats` |
| storage | http://localhost:8080 | `/`, `/health`, `/config` |
| mosquitto | tcp://localhost:1883, ws://localhost:9001 | MQTT / MQTT-over-WebSockets |

## Konfiguracija

Sve knob-ove drži `.env` (vidi `.env.example`). Projekat 3 radi u `BROKER_MODE=mqtt`
(Kafka kod je nasleđen iz P2 ali se ne pokreće).
