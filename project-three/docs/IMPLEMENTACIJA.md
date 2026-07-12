# Projekat 3 — Dnevnik implementacije

Prati šta je stvarno urađeno, po fazama. Plan (ciljna slika) je u [../PLAN.md](../PLAN.md);
ovaj dokument beleži realizaciju i odluke donete usput. Ažurira se posle svake faze.

**Status:** Faze 0–6 završene (puna integracija kroz Docker Compose, verifikovana end-to-end).
Ostaje: Faza 7 (polish + GitHub).

---

## 1. Pregled i arhitektura

Projekat 3 nadograđuje event-driven sistem iz Projekta 2 (Ingestion → Storage → Analytics nad
MQTT-om) dodavanjem dva nova sloja analize: **eKuiper** (CEP) i **MaaS** (ML servis). Radi se
isključivo u MQTT modu (Mosquitto); Kafka kod je nasleđen ali se ne pokreće.

### Kompletno rešenje (dijagram)

![Dijagram arhitekture Projekta 3](images/architecture-diagram.png)

> Svi servisi (zeleno) i MQTT topici (žuto) sa dijagrama su implementirani i verifikovani
> end-to-end u Fazi 6 (§9).

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
| `iot/stored` | Storage | Analytics, eKuiper | perzistirana očitavanja + `storedAt` |
| `iot/events` | eKuiper | Analytics | CEP događaji od interesa |
| `iot/analytics` | Analytics | web (Faza 5) | objedinjeni rezime `ANALYTICS_SUMMARY` |

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

## 6. Faza 4 — Analytics++ (integracija CEP + ML)

Analytics je proširen da objedini tri izvora analize i izloži jedinstveni rezultat. Ovo je
integraciona faza u kojoj se eKuiper događaji i MaaS predikcije spajaju.

### 6.1 Šta radi

1. Pretplata na **`iot/stored`** (očitavanja) i **`iot/events`** (eKuiper CEP) preko jednog MQTT
   klijenta (`mqtt-bus.js`, multi-topic subscribe + publish).
2. **Tumbling window** (10 s) računa prosek/min/max temperature + latenciju i akumulira
   **prosečno očitavanje** po svim senzorima.
3. Po zatvaranju prozora zove **MaaS `/predict`** sa prosečnim očitavanjem → klasa kvaliteta
   vazduha. Robustno: timeout + graceful degradacija (`maas-client.js`; ako MaaS padne,
   predikcija je `null`, servis nastavlja).
4. Objedinjeni **`ANALYTICS_SUMMARY`** (prozor + ML klasa + alarmi) se publikuje na
   **`iot/analytics`** (ulaz za web dashboard).

### 6.2 Novi REST endpointi (3001)

`/events` (CEP događaji + brojači po tipu), `/predictions` (MaaS predikcije), `/alerts`
(objedinjeni alarmi: temperaturni prag + kritični CEP + ML `unhealthy`). `/health` sada sadrži i
broj događaja/predikcija i status MaaS-a.

### 6.3 Odluke

- Analytics je u P3 **MQTT-native** — Kafka putanja (`kafka-consumer.js`) i `kafkajs` zavisnost
  su uklonjeni iz ovog servisa (eKuiper i tok događaja su isključivo MQTT).
- MaaS se zove **jednom po prozoru** (ne po svakoj poruci) da se ne preplavi.

### 6.4 Verifikacija (stvarno pokrenuto lokalno)

- `node --check` prolazi za sva 4 modula; `docker compose config` validan.
- **Integracioni test** protiv realnog MaaS-a (uvicorn): `maas-client.predict()` vraća klasu;
  window prosek tačan (avgTemp=25 za ulaze 20/30); `onFlush → MaaS → klasa` radi; MaaS status
  `reachable`, 0 grešaka. Runtime kroz Docker po dogovoru nije pokretan.

---

## 7. Faza 5 — Blazor web dashboard

Live dashboard (**Blazor Server**) koji objedinjeno prikazuje tok. Server-side MQTT pretplata
(MQTTnet) + push u UI preko SignalR-a — bez CDN-a i bez CORS-a, jedan kontejner.

### 7.1 Arhitektura

- `MqttBackgroundService` (hosted service): pretplata na `iot/analytics`, `iot/events`, `iot/stored`;
  puni `DashboardState`. Očitavanja (visoka učestalost) throttle-ovana na osvežavanje UI ~1×/s.
- `DashboardState` (singleton): deljeno stanje + `OnChange`; snapshot metode pod lock-om (bezbedno
  čitanje iz komponenti dok MQTT nit piše).
- `Dashboard.razor` (`@rendermode InteractiveServer`): pretplata na `OnChange`, `InvokeAsync(StateHasChanged)`.
- MaaS `GET /model/info` preko `HttpClient` (retry dok se MaaS ne podigne).

### 7.2 Prikaz

Kvalitet vazduha (ML klasa + verovatnoće, obojeno), tumbling window (prosek/min/max/latencija),
trend temperature (SVG sparkline), info o ML modelu, eKuiper CEP feed + brojači, live očitavanja,
baner alarma (prag / `unhealthy` / kritični CEP).

### 7.3 Verifikacija (stvarno pokrenuto lokalno)

- `dotnet build -c Release` → **0/0**.
- **Runtime smoke test** (bez Docker-a): app se diže (HTTP 200), server-side renderuje sve kartice,
  učitava `blazor.web.js` + `app.css`, i **ne pada** iako su MQTT/MaaS nedostupni (graceful degradacija
  + retry). `docker compose config` validan sa `web` servisom (port 8090→8080).

---

## 8. Da li bi trenirani model bio validan za neki drugi dataset?

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

## 9. Faza 6 — Finalna integracija (stvarni Docker runtime, end-to-end)

Do ove faze je svaka prethodna faza bila verifikovana samo statički
(`docker compose config`, `dotnet build`, `node --check`, lokalni smoke
testovi bez kontejnera). U Fazi 6 je ceo stack prvi put stvarno podignut
zajedno (`docker compose up -d --build`, svih 9 servisa) i propuštena je
kontinuirana simulacija saobraćaja (10 uređaja, 1 poruka/s, `forceAlert=true`,
>1 min) da se potvrdi da tok stvarno radi kraj-do-kraja, ne samo da se
kontejneri podignu.

### 9.1 Pronađeni i ispravljeni bugovi

Prvo pokretanje je otkrilo dva realna problema koja statička provera nije
mogla da uhvati — detaljno objašnjeni (simptom, uzrok, fix, verifikacija) u
**[../PROBLEMS.md](../PROBLEMS.md)**:

1. **Storage se rušio i nikad nije upisivao u bazu** — `PeriodicTimer` race
   u `MqttStorageWorker.ProcessBatchesAsync` bacao je `InvalidOperationException`
   čim bi poruke stizale brže od 1s (skoro uvek pod realnim saobraćajem), host
   se gasio (`BackgroundServiceExceptionBehavior=StopHost`), Docker ga je tiho
   restartovao — pa je `/health` izgledao zdravo dok `persistedMessages` nikad
   nije rastao. Ispravljeno prelaskom na `Stopwatch` + `Task.Delay`.
2. **Analytics → MaaS pozivi vraćali 404** — `.env` je imao trailing slash
   (`MAAS_URL=http://maas:8000/`), što je pravilo dupli `//predict`. Ispravljeno
   u `.env` + `maas-client.js` sada normalizuje URL bez obzira na env vrednost.

### 9.2 Verifikacija po tačkama iz README §4

| Provera | Rezultat |
|---|---|
| Storage upis + re-publish (`/health`) | `persistedMessages` = `receivedMessages`, `failedMessages: 0` (posle fix-a; pre fix-a zamrznuto) |
| `iot/stored` sadrži `storedAt` | OK |
| eKuiper pravila `Running` | sva 3 (`ruleHighTemp`, `ruleWindowHighTemp`, `rulePollutionSpike`) |
| `iot/events` CEP događaji | OK — `HIGH_TEMP` / `WINDOW_HIGH_TEMP` / `POLLUTION_SPIKE` okidaju pod `forceAlert` |
| Analytics `/events`, `/predictions`, `/alerts` | svi vraćaju sveže, objedinjene podatke |
| MaaS `/predict`, `/model/info` | OK, klase `good`/`moderate`/`unhealthy` sa verovatnoćama (posle fix-a; pre fix-a 404) |
| `iot/analytics` rezime po prozoru | OK (`ANALYTICS_SUMMARY`, sadrži `airQuality`, `tempAlert`) |
| Blazor web (`:8090`) | konektuje se na MQTT, poziva MaaS `/model/info`, servira dashboard (`200 OK`) |

Svi servisi ostaju živi i konzistentni pod kontinuiranim opterećenjem (nema
restart petlji, nema grešaka u logovima) posle primenjenih fix-eva.

### 9.3 Preostalo iz Faze 6 acceptance kriterijuma

- [ ] Dijagram arhitekture (dijagram iz §1 je već tu; čeka se eventualna
      dopuna/finalna verzija od kolege)
- [ ] Demo skripta (korak-po-korak scenario za prezentaciju)
- [ ] Screenshotovi Blazor dashboard-a tokom simulacije

---

## 10. Naredni koraci

| Faza | Sadržaj | Status |
|---|---|---|
| 3 | eKuiper (CEP): stream nad `iot/stored`, pravila → `iot/events` | ✅ završeno |
| 4 | Analytics++: konzum `iot/events` + poziv MaaS `/predict` + novi endpointi | ✅ završeno |
| 5 | Blazor web dashboard (čita `iot/analytics` / REST) | ✅ završeno |
| 6 | Finalna integracija (Docker runtime end-to-end, 2 bug-a nađena i ispravljena) | ✅ završeno (demo/screenshotovi preostaju) |
| 7 | Polish + čišćenje + push na GitHub sa opisom mikroservisa | ⏳ preostaje |
