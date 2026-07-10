# Projekat 3 — Plan realizacije i implementacije

> **Cilj (iz specifikacije):** Unaprediti **Analytics** mikroservis iz Projekta 2 tako da za
> analizu podataka koristi (a) **eKuiper** streaming/CEP servis preko MQTT brokera i (b) **MaaS**
> (Model-as-a-Service) mikroservis preko REST endpointa. Sve pokrenuti kao Docker kontejnere,
> uz Web/mobilnu aplikaciju, i postaviti na GitHub sa kratkim opisom mikroservisa.

Ovaj dokument je **plan**, ne implementacija. Kada bude odobren, krećemo po fazama (§7).

---

## 1. Zaključane odluke

| Tema | Odluka | Obrazloženje |
|---|---|---|
| MaaS ML zadatak | **Klasifikacija kvaliteta vazduha** (Good / Moderate / Unhealthy) | Akciona labela koju Analytics/UI lako prikazuju i alarmiraju; RandomForestClassifier je brz i tačan. |
| ML biblioteka | **scikit-learn** | Zadovoljava spec (scikit-learn / TF / PyTorch); najbrži trening i mali Docker image. |
| Frontend | **Blazor (.NET) Server** | Konzistentno sa Storage stackom; server-side MQTT pretplata + SignalR push u UI bez CORS-a. |
| Tok podataka | **Storage re-publikuje na `iot/stored`** | Verno tekstu specifikacije ("Data Storage publikuje na topic na koji je Analytics pretplaćen"); čist linearni pipeline. |
| Broker | **Samo MQTT (Mosquitto)** za P3 | Spec eksplicitno traži "preko MQTT brokera"; Kafka kod ostaje u servisima ali se ne pokreće. |

---

## 2. Polazno stanje (nasleđeno iz Projekta 2)

Trenutni tok (MQTT mod):

```
Ingestion (Node.js) --iot/readings--> [ Storage (.NET) -> Postgres ]
                                      [ Analytics (Node.js) -> tumbling window ]
```

Ključne činjenice na koje se oslanjamo:

- **Payload (kanonski model):**
  ```json
  {
    "deviceId": "device-00001",
    "timestamp": 1717000000000,
    "readings": {
      "co_gt": 1.2, "pt08_s1_co": 1050, "nmhc_gt": 150, "c6h6_gt": 8.4,
      "pt08_s2_nmhc": 900, "nox_gt": 120, "pt08_s3_nox": 800, "no2_gt": 90,
      "pt08_s4_no2": 1500, "pt08_s5_o3": 1100,
      "temperature": 21.5, "relative_humidity": 48.2, "absolute_humidity": 1.1
    }
  }
  ```
- **Dataset** `db/dataset/AirQualityUCI.csv` je već prisutan (isti kao P1/P2) — koristi se za trening MaaS modela.
- **Analytics** (Node.js): MQTT pretplata + `TumblingWindow` (10 s) nad `readings.temperature`, REST: `/health`, `/config`, `/window/stats`.
- **Storage** (.NET, MQTTnet): pretplata na `iot/readings`, batch upis (500) u Postgres. Trenutno **ne** publikuje nigde.
- **Mosquitto**: listener samo na `1883` (TCP). WebSocket listener treba dodati za web klijente.

> **Napomena o izolaciji:** Projekat 3 je **samostalan** folder (`project-three/`). Servise iz P2
> (ingestion, storage, analytics) **kopiramo** u `project-three/services/` i tamo ih razvijamo, da
> Projekat 2 ostane netaknut kao zaseban ocenjivan artefakt.

---

## 3. Ciljna arhitektura (Projekat 3)

```
                         ┌─────────────────────────────────────────────────────────┐
Ingestion (Node.js)      │                    Mosquitto (MQTT)                     │
   simulira uređaje ─────┼─► iot/readings                                          │
                         │        │                                                │
                         │        ▼                                                │
                  Storage (.NET) ── upis u Postgres                                │
                         │        └─ re-publish ─► iot/stored ──┬──────────────┐   │
                         │                                      │              │   │
                         │                                      ▼              ▼   │
                         │                            eKuiper (CEP)      Analytics (Node.js)
                         │                            pravila (SQL)      ├─ tumbling window
                         │                                │              ├─ MaaS REST /predict
                         │                                ▼              ├─ konzumira iot/events
                         │                            iot/events ───────►┘   
                         │                                                   │
                         └───────────────────────────────────────────────── │ ──┘
                                                                             │ (REST/SSE + MQTT-WS)
   MaaS (FastAPI + scikit-learn)  ◄── REST /predict ── Analytics            ▼
     /predict /health /model/info                                   Blazor Web App (dashboard)
```

**Topici (MQTT):**

| Topic | Producer | Consumer(i) | Sadržaj |
|---|---|---|---|
| `iot/readings` | Ingestion | Storage | sirova očitavanja (kanonski payload) |
| `iot/stored` | Storage | Analytics, eKuiper | perzistirana/validirana očitavanja |
| `iot/events` | eKuiper | Analytics | CEP događaji od interesa |
| `iot/analytics` *(opciono)* | Analytics | Web app | objedinjeni rezime (alarmi + predikcije) |

---

## 4. Komponente — detaljan opis

### 4.1 Storage servis (.NET) — dodati re-publish

**Izmena:** posle uspešnog batch upisa u Postgres, re-publikovati svako očitavanje na `iot/stored`.

- Dodati `MqttReadingPublisher` (MQTTnet klijent, već je dependency) i pozivati ga u `FlushAsync`
  nakon `writer.PersistAsync(...)`.
- Novi config: `MQTT_STORED_TOPIC=iot/stored`, `STORAGE_REPUBLISH=true` (feature flag).
- Payload na `iot/stored` = originalni payload + `storedAt` (server timestamp) radi merenja latencije.
- QoS 1; ne blokirati upis ako publish padne (best-effort, samo log/metric).
- Kafka grana ostaje nepromenjena (ne koristi se u P3).

**Acceptance:** `mosquitto_sub -t iot/stored` prikazuje poruke kad Ingestion generiše saobraćaj.

### 4.2 eKuiper (CEP) — novi servis

**Image:** `lfedge/ekuiper:latest` (REST API na `9081`, radi u istoj Docker mreži).

**Stream** (nad `iot/stored`), primer definicije:
```sql
CREATE STREAM iotStream (
  deviceId  STRING,
  timestamp BIGINT,
  readings  STRUCT(temperature FLOAT, nox_gt FLOAT, no2_gt FLOAT,
                   co_gt FLOAT, c6h6_gt FLOAT, relative_humidity FLOAT)
) WITH (DATASOURCE="iot/stored", FORMAT="json", TYPE="mqtt", SHARED="true");
```

**Pravila (CEP) — bar 3–4, svako emituje na `iot/events`:**

1. **HIGH_TEMP** — trenutni prag: `readings->temperature > 40`.
2. **WINDOW_HIGH_TEMP** — agregacija: `AVG(readings->temperature) > 35` nad `TUMBLINGWINDOW(ss, 10)`.
3. **POLLUTION_SPIKE** — kompozitni uslov: `readings->nox_gt > 400 AND readings->no2_gt > 200`.
4. **TEMP_SURGE** *(opciono)* — nagli skok preko `LAG`/prozora (rate-of-change).

**Sink:** MQTT → `iot/events`, struktura događaja:
```json
{ "type": "POLLUTION_SPIKE", "deviceId": "device-00007", "value": 512.3,
  "window": "10s", "severity": "warning", "ts": 1717000000000, "rule": "pollutionSpike" }
```

**Provisioning:** `ekuiper/streams/*.json` + `ekuiper/rules/*.json`; init skripta
`ekuiper/provision.sh` (curl na `:9081`) ili poseban `ekuiper-init` compose servis koji čeka
`healthy` eKuiper pa POST-uje definicije. Definicije idu u git (reproducibilnost).

**Acceptance:** `mosquitto_sub -t iot/events` prikazuje događaje kada saobraćaj pređe prag.

### 4.3 MaaS (Python + FastAPI + scikit-learn) — novi servis

**ML zadatak:** klasifikacija kvaliteta vazduha u 3 klase: `good` / `moderate` / `unhealthy`.

**Podaci i labeliranje:**
- Ulaz: `AirQualityUCI.csv`; zameniti `-200` (marker nedostajuće vrednosti) sa NaN, imputacija (median) ili drop.
- **Features (X):** senzorski odzivi koje simulator emituje — `pt08_s1_co, pt08_s2_nmhc, pt08_s3_nox,
  pt08_s4_no2, pt08_s5_o3, temperature, relative_humidity, absolute_humidity`.
- **Label (y):** izvedena iz referentnih zagađivača (`co_gt, nox_gt, no2_gt, c6h6_gt`) po pragovima
  (orijentacione vrednosti, tuniraju se za balans klasa):
  - `good`: CO(GT) < 2 **i** NO2(GT) < 100 **i** NOx(GT) < 150
  - `unhealthy`: CO(GT) > 5 **ili** NO2(GT) > 200 **ili** NOx(GT) > 400
  - `moderate`: sve ostalo
- Priča modela: "jeftini MOX senzori (PT08.*) predviđaju klasu koju bi dodelili referentni instrumenti".

**Trening pipeline** (`app/train.py`):
- `train/val/test` split (npr. 70/15/15, stratifikovano), `StandardScaler` + `RandomForestClassifier`.
- Metrike: accuracy, precision/recall/F1 po klasi, confusion matrix → u `model/metadata.json`.
- Artefakti: `model/model.joblib`, `model/metadata.json` (komituju se ili se generišu u build-u).

**REST API** (FastAPI, port `8000`):

| Metoda | Ruta | Opis |
|---|---|---|
| `POST` | `/predict` | jedno očitavanje → `{class, probabilities, model_version}` |
| `POST` | `/predict/batch` | lista očitavanja → lista predikcija |
| `GET` | `/health` | status + da li je model učitan |
| `GET` | `/model/info` | tip modela, verzija, features, klase, trening metrike |

**Docker:** `python:3.12-slim`, `requirements.txt` (fastapi, uvicorn, scikit-learn, pandas, joblib);
model se učitava pri startu. Skripta `make train` / `docker build` faza za retrening.

**Acceptance:** `curl -X POST /predict` sa uzorkom vraća validnu klasu; `/model/info` prikazuje metrike.

### 4.4 Analytics (Node.js) — unapređenje

**Nove odgovornosti:**
1. Pretplata na `iot/stored` (umesto/pored `iot/readings`) — ulaz podataka.
2. Druga MQTT pretplata na `iot/events` — konzumira eKuiper CEP događaje.
3. **MaaS klijent:** za reprezentativno očitavanje po prozoru (ili sample svakih N poruka) poziva
   `POST http://maas:8000/predict`; čuva predikciju + verovatnoće. (Poziv po prozoru da se MaaS ne preplavi.)
4. Objedinjavanje: tumbling window statistika + poslednji eKuiper događaji + poslednje MaaS predikcije
   → enriched log/alarm; opciono publish rezimea na `iot/analytics`.

**REST endpointi (dopuna):**

| Ruta | Opis |
|---|---|
| `GET /window/stats` | postojeća window statistika |
| `GET /events` | poslednjih N eKuiper događaja |
| `GET /predictions` | poslednjih N MaaS predikcija |
| `GET /alerts` | agregirani alarmi (prag + CEP + ML klasa `unhealthy`) |
| `GET /health`, `/config` | postojeći |

**Acceptance:** `/events` i `/predictions` vraćaju sveže podatke; log prikazuje kombinovane alarme.

### 4.5 Web app (Blazor Server) — novi servis

- **Blazor Server** kontejner (.NET SDK build → runtime image).
- Server-side MQTT pretplata (MQTTnet) na `iot/stored` i `iot/events`; push u UI preko SignalR (bez CORS-a).
- Poziva Analytics REST (`/events`, `/predictions`, `/alerts`, `/window/stats`) i MaaS `/model/info`.
- **Dashboard:** live tabela/grafik očitavanja, feed eKuiper događaja, MaaS klasa kvaliteta vazduha
  (obojeni indikator: zelena/žuta/crvena), alarm baner, window statistika.
- Mosquitto dobija i **WebSocket listener `9001`** (za buduće JS/WASM klijente; Blazor Server koristi TCP 1883 sa servera).

**Acceptance:** stranica u browseru prikazuje live podatke, događaje i ML klasu koji se menjaju u realnom vremenu.

---

## 5. Docker Compose (project-three)

Servisi: `postgres`, `mosquitto` (+WS 9001), `ingestion`, `storage`, `analytics`, `ekuiper`,
`ekuiper-init`, `maas`, `web`. (Kafka se **ne** pokreće u P3.)

Zavisnosti (healthcheck/depends_on):
```
postgres, mosquitto  →  storage, analytics, ingestion
mosquitto            →  ekuiper  →  ekuiper-init (provisioning)
maas (healthy)       →  analytics (soft; retry na /predict)
analytics, mosquitto →  web
```

Portovi (host): postgres 5432, mosquitto 1883 + 9001, ingestion 3000, analytics 3001,
storage 8080, ekuiper 9081, maas 8000, web 8090.

---

## 6. Predložena struktura foldera

```
project-three/
├── PLAN.md                      # ovaj dokument
├── README.md                    # kratak opis mikroservisa (spec tačka 5)
├── docker-compose.yml
├── .env / .env.example
├── brokers/mqtt/mosquitto.conf  # + WebSocket listener 9001
├── db/                          # migracije + dataset (iz P2)
├── ekuiper/
│   ├── streams/iotStream.json
│   ├── rules/highTemp.json, windowHighTemp.json, pollutionSpike.json
│   └── provision.sh
├── services/
│   ├── ingestion/               # kopija iz P2 (bez izmena)
│   ├── storage/                 # + MQTT re-publish na iot/stored
│   ├── analytics/               # + eKuiper konzum + MaaS klijent + novi endpointi
│   └── maas/                    # FastAPI + scikit-learn (train.py, app/, model/)
└── web/                         # Blazor Server dashboard
```

---

## 7. Faze implementacije (build order + acceptance)

| Faza | Sadržaj | Acceptance kriterijum |
|---|---|---|
| **0. Scaffolding** | Kreirati `project-three/`, kopirati P2 servise, bazni compose (postgres, mosquitto+WS, ingestion, storage, analytics) u MQTT modu. | Stack se diže; Ingestion→Storage→Postgres i Analytics window rade kao u P2. |
| **1. Storage re-publish** | MQTT publisher u Storage → `iot/stored`; Analytics prebačen na `iot/stored`. | `mosquitto_sub -t iot/stored` prikazuje poruke; Analytics window i dalje radi. |
| **2. MaaS** | Dataset prep + labeliranje + `train.py` + model artefakt + FastAPI + Docker. | `/predict` vraća klasu; `/model/info` prikazuje metrike; kontejner se diže. |
| **3. eKuiper** | Servis + stream + 3–4 pravila + provisioning; sink na `iot/events`. | `mosquitto_sub -t iot/events` prikazuje CEP događaje pri prelasku pragova. |
| **4. Analytics++** | Pretplata na `iot/events`, MaaS klijent, `/events` `/predictions` `/alerts`. | Endpointi vraćaju sveže podatke; kombinovani alarmi u logu. |
| **5. Blazor web** | MQTT pretplata + Analytics/MaaS REST + live dashboard; Docker. | Browser prikazuje live očitavanja, događaje, ML klasu u realnom vremenu. |
| **6. Integracija + docs** | Ceo compose zajedno; README sa opisom mikroservisa; demo skripta; screenshotovi. | `docker compose up` diže sve; end-to-end demo prolazi. |
| **7. Polish + GitHub** | Kratak izveštaj/opis, čišćenje, push na GitHub. | Repo sa opisom mikroservisa (spec tačka 5). |

---

## 8. Ugovori (JSON kontrakti) — sažetak

- **`iot/stored`** = kanonski payload + `"storedAt": <ms>`.
- **`iot/events`** = `{type, deviceId, value, window, severity, ts, rule}`.
- **MaaS `/predict` request** = `{ "features": { pt08_s1_co, pt08_s2_nmhc, pt08_s3_nox, pt08_s4_no2,
  pt08_s5_o3, temperature, relative_humidity, absolute_humidity } }`.
- **MaaS `/predict` response** = `{ "class": "moderate", "probabilities": {"good":..,"moderate":..,
  "unhealthy":..}, "model_version": "1.0.0" }`.

---

## 9. Rizici i mitigacije

| Rizik | Mitigacija |
|---|---|
| eKuiper JSON pristup ugnježđenom `readings.*` | Definisati `STRUCT(...)` u stream šemi; testirati mapiranje ranije (Faza 3). |
| MaaS preplavljen pozivima na visokom throughput-u | Analytics zove MaaS **po prozoru** ili sample-uje; `/predict/batch` opcija. |
| Nedostajuće vrednosti (`-200`) kvare model | Čišćenje + imputacija u `train.py`; validirati distribuciju klasa. |
| Nebalansirane klase kvaliteta vazduha | Tuniranje pragova labeliranja; `class_weight="balanced"`; izveštaj po klasi. |
| Blazor real-time složenost | Blazor **Server** + SignalR (server drži MQTT pretplatu) umesto WASM. |
| eKuiper provisioning race (servis još nije spreman) | `ekuiper-init` čeka healthcheck pre POST-a definicija. |

---

## 10. Mapiranje na zahteve specifikacije

| Spec tačka | Pokriveno u planu |
|---|---|
| 1a — Analytics koristi eKuiper CEP preko MQTT | §4.2, §4.4, Faza 3–4 |
| 1b — Analytics koristi MaaS REST | §4.3, §4.4, Faza 2 i 4 |
| 2 — eKuiper na istom topic-u, pravila → novi topic → Analytics | §3 (topici), §4.2 |
| 3 — MaaS: Python + FastAPI + ML (scikit-learn), train/val/test | §4.3, Faza 2 |
| 4 — Docker kontejneri + Web/mobilna app | §5, §4.5, Faza 5–6 |
| 5 — GitHub + kratak opis mikroservisa | §6 (README), Faza 7 |

---

## 11. Otvorena pitanja za kasnije (ne blokiraju start)

- Da li Analytics dodatno da publikuje `iot/analytics` rezime, ili je dovoljan REST/SSE za web? (default: REST + opciono MQTT-WS).
- Da li web app treba i istorijski prikaz iz Postgres-a (query kroz Storage/novi read endpoint) ili samo live? (default: live).
- Tačni pragovi za eKuiper pravila i labeliranje — finalizuju se na realnim uzorcima u Fazi 2–3.
```
