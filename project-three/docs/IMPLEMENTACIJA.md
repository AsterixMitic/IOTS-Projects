# Projekat 3 — Dnevnik implementacije

Prati šta je stvarno urađeno, po fazama. Plan (ciljna slika) je u [../PLAN.md](../PLAN.md);
ovaj dokument beleži realizaciju i odluke donete usput. Ažurira se posle svake faze.

**Status:** Faze 0–3 završene. U toku: Faza 4 (Analytics++).

---

## 1. Pregled i arhitektura

Projekat 3 nadograđuje event-driven sistem iz Projekta 2 (Ingestion → Storage → Analytics nad
MQTT-om) dodavanjem dva nova sloja analize: **eKuiper** (CEP) i **MaaS** (ML servis). Radi se
isključivo u MQTT modu (Mosquitto); Kafka kod je nasleđen ali se ne pokreće.

### Kompletno rešenje (dijagram)

```mermaid
flowchart LR
    ING["Ingestion<br/>(Node.js)<br/>simulira uređaje"]
    STOR["Storage<br/>(.NET)<br/>batch upis + re-publish"]
    PG[("PostgreSQL")]
    EK["eKuiper<br/>CEP pravila (SQL)"]
    AN["Analytics<br/>(Node.js)<br/>tumbling window + orkestracija"]
    MAAS["MaaS<br/>(FastAPI + scikit-learn)<br/>klasifikacija vazduha"]
    WEB["Blazor Web<br/>live dashboard"]

    T1(["iot/readings"])
    T2(["iot/stored"])
    T3(["iot/events"])

    ING -->|publish| T1
    T1 -->|subscribe| STOR
    STOR -->|upis| PG
    STOR -->|re-publish| T2
    T2 -->|subscribe| AN
    T2 -->|subscribe| EK
    EK -->|CEP događaji| T3
    T3 -->|subscribe| AN
    AN -->|"REST /predict"| MAAS
    AN -->|"REST / SSE"| WEB
    T2 -.->|MQTT-WS| WEB
    T3 -.->|MQTT-WS| WEB

    classDef done fill:#d4edda,stroke:#28a745,color:#155724;
    classDef todo fill:#eef1f4,stroke:#adb5bd,color:#495057,stroke-dasharray:5 5;
    classDef topic fill:#fff3cd,stroke:#ffc107,color:#856404;
    class ING,STOR,PG,AN,MAAS,EK done;
    class WEB todo;
    class T1,T2,T3 topic;
```

> Legenda: **pun zeleni okvir** = implementirano (Faze 0–2), **isprekidan sivi** = predstoji
> (Faze 3–5), **žuti čvorovi** = MQTT topici.

Tok podataka (trenutno stanje, posle Faze 2):

```
Ingestion (Node.js) ─iot/readings─► Storage (.NET) ─► PostgreSQL
                                        └─ re-publish ─► iot/stored ─► Analytics (Node.js, tumbling window)

MaaS (Python/FastAPI + scikit-learn):  /health  /model/info  /predict  /predict/batch
```

U narednim fazama: eKuiper se pretplaćuje na `iot/stored`, emituje CEP događaje na `iot/events`;
Analytics ih konzumira i zove MaaS `/predict`; Blazor web prikazuje sve.

MQTT topici:

| Topic | Producer | Consumer | Sadržaj |
|---|---|---|---|
| `iot/readings` | Ingestion | Storage | sirova očitavanja (`{deviceId, timestamp, readings{13 senzora}}`) |
| `iot/stored` | Storage | Analytics (i uskoro eKuiper) | perzistirana očitavanja + `storedAt` |
| `iot/events` | eKuiper *(Faza 3)* | Analytics | CEP događaji od interesa |

---

## 2. Faza 0 — Scaffolding

Samostalan `project-three/` (P2 ostaje netaknut). Servisi iz P2 (ingestion, storage, analytics)
kopirani i očišćeni od build artefakata.

- `docker-compose.yml`: `postgres` (+ auto-seed migracijom `0001` u `initdb.d`), `mosquitto`
  (+ **WebSocket listener 9001** za web klijente), `ingestion`, `storage`, `analytics` — MQTT only.
- `.env` / `.env.example` (konvencija: `.env` lokalno, u git ide `.env.example`).
- `README.md`, `.gitignore`, `PLAN.md`.

**Rezultat:** čist MQTT skelet, `docker compose config` validan.

---

## 3. Faza 1 — Storage re-publish na `iot/stored`

Storage sada, **posle uspešnog batch upisa u bazu**, re-publikuje svako očitavanje na `iot/stored`
(topic na koji se pretplaćuju Analytics i eKuiper). Verno tekstu specifikacije ("Data Storage
publikuje na topic na koji je Analytics pretplaćen").

Ključne izmene (.NET):

- `Infrastructure/IStoredReadingPublisher.cs` — apstrakcija (ista konvencija kao `IStorageWorker`).
- `Infrastructure/MqttStoredReadingPublisher.cs` — MQTTnet publisher; lazy-connect, QoS 1,
  **best-effort** (greška u publish-u ne ruši upis). Payload = `{deviceId, timestamp, readings, storedAt}`;
  `storedAt` omogućava merenje end-to-end latencije nizvodno, a originalni `timestamp` je očuvan.
- `MqttStorageWorker` poziva `publisher.PublishAsync(batch)` u `FlushAsync` posle `PersistAsync`.
- Config: `MQTT_STORED_TOPIC` (`iot/stored`), `STORAGE_REPUBLISH` flag; metrika `republishedMessages`.
- Analytics prevezan na `iot/stored` (kroz compose env, bez izmene koda — payload zadržava
  `readings.temperature` i `timestamp`).

**Verifikacija:** `dotnet build -c Release` → **0 warning, 0 error**. MQTTnet razrešen `4.3.7.1207`
(`IMqttClient` je tip iz te biblioteke). Runtime kroz Docker po dogovoru nije testiran.

---

## 4. Faza 2 — MaaS (Model-as-a-Service)

Python + **FastAPI** + **scikit-learn** mikroservis za **klasifikaciju kvaliteta vazduha**
(`good` / `moderate` / `unhealthy`).

### 4.1 Model i podaci

- **Features (X):** 8 senzorskih odziva koje simulator emituje — `pt08_s1_co, pt08_s2_nmhc,
  pt08_s3_nox, pt08_s4_no2, pt08_s5_o3, temperature, relative_humidity, absolute_humidity`.
- **Label (y):** izvedena iz referentnih zagađivača (`co_gt, no2_gt, nox_gt`) po pragovima
  (`good` ako su svi ispod praga, `unhealthy` ako je bar jedan iznad, inače `moderate`).
- **Podaci:** Air Quality UCI (`db/dataset/AirQualityUCI.csv`); `-200` → NaN; imputacija medijanom.
- **Algoritam:** `RandomForestClassifier` (150 stabala, `max_depth=20`, `min_samples_leaf=5`,
  `class_weight="balanced"`) u sklearn `Pipeline` (imputer + klasifikator).

### 4.2 Rezultati treninga

| Metrika | Vrednost |
|---|---|
| Redova posle čišćenja | 9357 |
| Redova sa labelom | 7258 |
| Distribucija klasa | good 2229 / moderate 2335 / unhealthy 2694 (balansirano) |
| Split | 70/15/15 stratifikovano (train 5080 / val 1089 / test 1089) |
| **Validation accuracy** | **0.8705** |
| **Test accuracy** | **0.8494** |
| F1 po klasi (test) | good 0.87 / moderate 0.79 / unhealthy 0.89 |
| Veličina artefakta | 2.7 MB (`compress=3`) |

Metrike i confusion matrix su u `model/metadata.json`.

### 4.3 REST API (port 8000)

`GET /health`, `GET /model/info`, `POST /predict`, `POST /predict/batch`. Zahtev prosleđuje
`readings` objekat (dodatni ključevi se ignorišu). Odgovor: klasa + verovatnoće + verzija modela.

### 4.4 Verifikacija (stvarno pokrenuto lokalno)

- Trening odrađen, artefakti (`model.joblib`, `metadata.json`) generisani i komitovani.
- `uvicorn` servis: `/health` → ok; visoki senzorski odzivi → `unhealthy` (0.99);
  niski → `good` (0.99); `/model/info` vraća metrike.
- `docker compose config` validan sa `maas` servisom. Docker build nije pokretan (dogovor).

---

## 5. Faza 3 — eKuiper (CEP)

Dodat **eKuiper** streaming/CEP engine koji se pretplaćuje na `iot/stored`, primenjuje pravila i
emituje događaje od interesa na `iot/events` (Analytics ih preuzima u Fazi 4).

### 5.1 Konfiguracija

- Servis `ekuiper` (`lfedge/ekuiper:1.14`), REST API na portu **9081**.
- MQTT izvor konfigurisan env override-om: `MQTT_SOURCE__DEFAULT__SERVER=tcp://mosquitto:1883`.
- Jednokratni init kontejner `ekuiper-init` (`curlimages/curl`) čeka REST API i registruje
  stream + pravila iz `ekuiper/` foldera (idempotentno — DELETE pa POST).

### 5.2 Stream

`iotStream` nad `iot/stored` (FORMAT json), sa STRUCT šemom za polja koja pravila koriste
(`temperature, nox_gt, no2_gt, co_gt, c6h6_gt, relative_humidity`).

### 5.3 CEP pravila → `iot/events`

| Pravilo | Tip događaja | Logika |
|---|---|---|
| `ruleHighTemp` | `HIGH_TEMP` | trenutni prag: `temperature > 40` |
| `ruleWindowHighTemp` | `WINDOW_HIGH_TEMP` | agregacija: `AVG(temperature) > 35` nad `TUMBLINGWINDOW(ss,10)` |
| `rulePollutionSpike` | `POLLUTION_SPIKE` | kompozit: `nox_gt > 400 AND no2_gt > 200` |

Svaki događaj se šalje kao zaseban JSON (`sendSingle`) sa poljima `type, severity, rule` +
relevantnim vrednostima, na MQTT sink `iot/events`.

### 5.4 Verifikacija

`docker compose config` validan sa `ekuiper`/`ekuiper-init`; svi JSON fajlovi (stream + pravila)
sintaksno ispravni; `provision.sh` ima LF (radi u Alpine `sh`). Runtime kroz Docker po dogovoru
nije pokretan — provera: `mosquitto_sub -t iot/events` uz simulaciju sa `forceAlert` treba da
prikaže `HIGH_TEMP` / `WINDOW_HIGH_TEMP` događaje.

---

## 6. Da li bi trenirani model bio validan za neki drugi dataset?

Kratko: **artefakt (istrenirani model) je specifičan za ovaj dataset i ne bi se dobro preneo na
proizvoljan drugi dataset — ali metodologija i kod (`train.py`) jesu ponovo upotrebljivi uz
retrening.**

**Zašto artefakt ne generalizuje kao takav:**

1. **Vezan za konkretne senzore i kalibraciju.** Features su tačno određeni MOX odzivi
   (PT08.S1–S5) sa opsezima i kalibracijom ove stanice (Italijanski roadside, UCI). Drugi dataset
   sa drugačijim senzorima/jedinicama/opsezima ne bi odgovarao ulaznom prostoru modela.
2. **Pragovi labeliranja su domenski.** Labela je izvedena iz `co_gt` (mg/m³), `no2_gt` (µg/m³),
   `nox_gt` (ppb) sa pragovima podešenim na distribucije ovog dataseta. Drugi grad/period/jedinice
   → drugačije distribucije → isti pragovi daju iskrivljenu labelu.
3. **Domain shift / drift senzora.** MOX senzori driftuju vremenom i razlikuju se po uređaju;
   čak i isti tip senzora u drugom okruženju/sezoni pomera raspodelu (kovarijantni pomak).

**Šta jeste prenosivo:**

- **Pipeline i pristup** (čišćenje `-200`, imputacija, izvođenje labele iz referentnih zagađivača,
  RF klasifikator, 70/15/15 evaluacija) rade na bilo kom sličnom air-quality datasetu.
  `train.py` je parametrizovan putanjom do CSV-a — dovoljno je uperiti ga na nove podatke i retrening.
- **Isti šematski dataset** (iste kolone/jedinice, npr. druga UCI-slična stanica) bi radio sa
  smanjenom tačnošću i imao koristi od retreninga/fine-tuninga i re-kalibracije pragova.
- **Za strukturno drugačiji dataset** treba: re-mapirati features, ponovo izvesti labele
  (nove pragove/definiciju klasa), po potrebi per-senzor normalizaciju ili transfer learning.

**Bitna napomena za ovaj projekat:** unutar Projekta 3 model **jeste validan**, jer Ingestion
simulator generiše vrednosti u istim opsezima kao ovaj dataset (opsezi senzora u simulatoru su
izvedeni iz AirQualityUCI). Ceo sistem koristi isti model podataka, pa je model konzistentan sa
tokom koji analizira.

---

## 7. Naredni koraci

| Faza | Sadržaj | Status |
|---|---|---|
| 3 | eKuiper (CEP): stream nad `iot/stored`, pravila → `iot/events` | ✅ završeno |
| 4 | Analytics++: konzum `iot/events` + poziv MaaS `/predict` + novi endpointi | ⏳ u toku |
| 5 | Blazor web dashboard | ⬜ |
| 6 | Integracija, README opis mikroservisa, demo | ⬜ |
