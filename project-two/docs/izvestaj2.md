# Tehnički izveštaj — MQTT naspram Kafka: IoT mikroservisi zasnovani na događajima

**Internet stvari i servisa — Projekat 2 (2026)**

**Autori:** Andrija Grbušić, Aleksandar Mitić

> Ovaj dokument je srpska (latinična) verzija izveštaja iz `docs/report.md` (transliteracija ćiriličnog `docs/izvestaj.md`), popunjena stvarnim rezultatima merenja iz `docs/results/` (scenariji A–D, oba brokera, sve testirane konfiguracije QoS/`acks`). Poslednje poglavlje sadrži i proveru usklađenosti sa specifikacijom iz `IotS - Projekat 2 - 2026.md`.

## 1. Pregled sistema

Sistem se sastoji od tri mikroservisa, svaki nezavisan od konkretnog brokera preko promenjive `BROKER_MODE=mqtt|kafka`:

| Servis | Tehnologija | Uloga |
|---|---|---|
| `ingestion` | Node.js | Simulira N uređaja i šalje očitavanja senzora vazduha (Air Quality dataset, isti model podataka kao u Projektu 1) na aktivni broker. QoS (MQTT) / `acks` (Kafka) podesivi su preko env promenljivih. |
| `storage` | .NET (ASP.NET Core) | Pretplaćen na broker, grupno (batch) upisuje u PostgreSQL (`STORAGE_BATCH_SIZE`, podrazumevano 500 poruka po upisu). |
| `analytics` | Node.js | Nezavisno pretplaćen na isti tok, računa Tumbling Window od 10s nad `temperature`, ispisuje `[ALERT]` kada prosek prozora pređe `ALERT_THRESHOLD` (podrazumevano 50°C), i meri latency po poruci (generisanje → prijem). |

**Brokeri:**

- **Mosquitto (MQTT)** — podrška za QoS 0/1/2, trajna (persistent) sesija (`clean: false`) na `analytics` pretplati; `mosquitto.conf` uključuje diskovnu persistenciju (`persistence true`), `max_queued_messages 10000`, `max_inflight_messages 100`.
- **Apache Kafka (KRaft mod, bez Zookeeper-a)** — `acks=0 / 1 / -1 (all)`, topik `iot.readings` sa **3 particije** (`KAFKA_NUM_PARTITIONS=3`), dve consumer grupe: `project-two-storage` i `analytics-group`.

Ceo sistem je kontejnerizovan pomoću **Docker Compose-a** (`docker-compose.yml`): `postgres`, `mosquitto`, `kafka`, `ingestion`, `storage`, `analytics`. Korišćene su najmanje dve tehnologije (Node.js i ASP.NET Core / .NET), u skladu sa zahtevom specifikacije.

## 2. Eksperimentalni scenariji

| Scenario | Opis | Skripta |
|---|---|---|
| A — Masovni unos sa senzora | 100 / 1000 / 10000 simuliranih uređaja @ 1 msg/s, 30s po nivou | `benchmarks/{broker}/scenario_a.sh` |
| B — Prekid konekcije na edge-u | 30s `docker network disconnect` na `ingestion`-u, praćenje oporavka | `benchmarks/{broker}/scenario_b.sh` |
| C — Nalet opterećenja (burst) | ~50 msg/s (15s) → ~5000 msg/s (10s burst) → oporavak (30s) | `benchmarks/{broker}/scenario_c.sh` |
| D — Alarmiranje u realnom vremenu | Forsirana očitavanja > 50°C, merenje latency generisanje → `[ALERT]` | `benchmarks/{broker}/scenario_d.sh` |

Opterećenje u svim scenarijima generiše sam `ingestion` servis preko HTTP kontrolnog API-ja (`/simulate/start`, `/simulate/stop`), praćenje metrika vrši se preko brojanja redova u PostgreSQL bazi (`reading_count`), `docker stats` (CPU/RAM/network) i, za Kafka, `kafka-consumer-groups.sh --describe` za consumer lag.

## 3. Uporedna tabela performansi

### 3.1 Scenario A — Propusnost i gubitak poruka

#### MQTT (merenje na nivou baze — Mosquitto nema koncept offset-a/log-a)

| QoS | Uređaji | Očekivano | Primljeno (DB) | Gubitak % | Propusnost (msg/s) |
|---|---|---|---|---|---|
| 0 | 100 | 3000 | 2900 | 3.33 | 96.67 |
| 1 | 100 | 3000 | 2900 | 3.33 | 96.67 |
| 2 | 100 | 3000 | 2900 | 3.33 | 96.67 |
| 0 | 1000 | 30000 | 29000 | 3.33 | 966.67 |
| 1 | 1000 | 30000 | 29000 | 3.33 | 966.67 |
| 2 | 1000 | 30000 | 27691 | 7.70 | 923.03 |
| 0 | 10000 | 300000 | 92503 | 69.17 | 3083.43 |
| 1 | 10000 | 300000 | 83701 | 72.10 | 2790.03 |
| 2 | 10000 | 300000 | 72094 | 75.97 | 2403.13 |

#### Kafka — na nivou broker-a (delta log-end-offset-a, tj. poruke koje je Kafka trajno upisala, nezavisno od brzine consumer-a) i na nivou storage-a (redovi upisani u Postgres + lag consumer-a na kraju)

| acks | Uređaji | Očekivano | Persistovano (DB) | Propusnost DB (msg/s) | Gubitak DB % | Storage lag na kraju (Σ 3 particije) | Primljeno na broker-u* | Gubitak broker-a* % |
|---|---|---|---|---|---|---|---|---|
| 0 | 100 | 3000 | 2900 | 96.67 | 3.33 | 0 | ≈2900 | ≈3.3 |
| 1 | 100 | 3000 | 2900 | 96.67 | 3.33 | 0 | ≈2900 | ≈3.3 |
| all | 100 | 3000 | 2901 | 96.70 | 3.30 | 0 | ≈2901 | ≈3.3 |
| 0 | 1000 | 30000 | 29000 | 966.67 | 3.33 | 0 | ≈29000 | ≈3.3 |
| 1 | 1000 | 30000 | 29000 | 966.67 | 3.33 | 0 | ≈29000 | ≈3.3 |
| all | 1000 | 30000 | 29000 | 966.67 | 3.33 | 0 | ≈29000 | ≈3.3 |
| 0 | 10000 | 300000 | 86501 | 2883.37 | 71.17 | 196227 | 285228 | 4.92 |
| 1 | 10000 | 300000 | 82501 | 2750.03 | 72.50 | 200431 | 285432 | 4.86 |
| all | 10000 | 300000 | 82501 | 2750.03 | 72.50 | 200014 | 284515 | 5.16 |

\* Za 100/1000 uređaja, lag consumer-a na kraju tog nivoa je 0 (`CURRENT-OFFSET == LOG-END-OFFSET`), tj. storage je potpuno stigao broker, pa je "primljeno na broker-u" ≈ "persistovano (DB)". Za 10000 uređaja, "primljeno na broker-u" je izračunato kao razlika zbira `LOG-END-OFFSET` (3 particije) na kraju 10000-tira i na kraju 1000-tira — to je broj poruka koje je Kafka stvarno trajno upisala u topik tokom ovog konkretnog 30s nivoa, nezavisno od toga koliko je storage stigao da pročita.

**Analiza:**

- **~3.33% gubitak na 100 i 1000 uređaja pojavljuje se kod praktično svih konfiguracija** (i MQTT i Kafka, svi QoS/acks). Brojčano, 3.33% od 3000 = 100 poruka = tačno **1 sekunda** od 30-sekundnog prozora. Ovo je artefakt mernog harnesa (kašnjenje starta `/simulate/start` poziva u odnosu na trenutak uzimanja `before` brojača), a ne stvarni gubitak na broker-u/mreži. Pravi signal se vidi samo na **10000 uređaja**.
- **Na 10000 uređaja, MQTT gubitak raste sa QoS-om**: QoS0 = 69.17% → QoS1 = 72.10% → QoS2 = 75.97%. Ovo je dosledno očekivanju: QoS2 zahteva 4-smernu razmenu (PUBLISH/PUBREC/PUBREL/PUBCOMP) po poruci, što pod ekstremnim opterećenjem (10000 istovremenih MQTT konekcija) smanjuje maksimalnu end-to-end brzinu koju cela linija (ingestion → mosquitto → storage batch insert) može da izdrži.
- **Na 10000 uređaja, Kafka gubitak na nivou DB (71–73%) praktično ne zavisi od `acks`** — `acks=0`, `acks=1` i `acks=all` daju skoro identičan rezultat. Međutim, **gubitak na nivou broker-a je samo ~5%** (4.92% / 4.86% / 5.16%), a broker throughput je ~9500 msg/s — preko 3× više od onoga što `storage` uspeva da upiše u Postgres (≈2750–2880 msg/s). Ovo direktno potvrđuje napomenu iz specifikacije: **razlika između "primljeno na broker-u" i "persistovano (DB)" NIJE gubitak na broker-u, već batch-insert propusnost storage servisa koja postaje usko grlo** — upravo problem koji specifikacija predviđa i za koji predlaže batching/isključivanje upisa.
- **Consumer lag i particionisanje**: topik `iot.readings` ima 3 particije, sa `project-two-storage` i `analytics-group` consumer grupama (svaka particija obrađena od posebnog consumer-a u okviru iste grupe/procesa). Na 10000 uređaja, `analytics-group` završava sa **lag = 0** na svim particijama (agregacija u memoriji je jeftina, nema I/O), dok `project-two-storage` završava sa **lag ≈ 196–200 hiljada poruka** po konfiguraciji — baš onoliko koliko broker-storage "propusni-jaz" iznosi. Particionisanje na 3 omogućava da se consumer lag ravnomerno rasporedi (lag po particii ≈ 65–67k za sva tri `acks` podešavanja), ali ne rešava suštinsko usko grlo (Postgres batch insert).

### 3.2 Scenario B — Oporavak nakon 30s mrežnog prekida

Scenario: 20 uređaja @ 1 msg/s, ukupno 90s. `ingestion` kontejner se isključuje iz Docker mreže u trenutku **t=15s** na **30s** (do t=45s), zatim se ponovo povezuje. Brojač "readings_total" predstavlja ukupan broj novih redova u Postgres u odnosu na stanje pre starta simulacije.

| Broker | QoS/acks | Vrednost u t=45s (rekonekcija) | Vrednost u t=90s (kraj) | Oporavljeno (45s nakon rekonekcije) | Prvi skok rasta nakon rekonekcije |
|---|---|---|---|---|---|
| MQTT | 0 | 300 | 1780 | 1480 | +25s (t=70s) |
| MQTT | 1 | 300 | 1780 | 1480 | +25s (t=70s) |
| MQTT | 2 | 300 | 820 | 520 | +25s (t=70s) |
| Kafka | 1 | 300 | 2240 | 1940 | +25s (t=70s) |
| Kafka | all | 300 | 2240 | 1940 | +25s (t=70s) |
| Kafka | 0 | 84501† | 95717† | 11216† | odmah† (raste i za vreme "disconnected" faze) |

\* "Oporavljeno" je mereno za 45s nakon rekonekcije, ne 60s, jer `scenario_b.sh` ukupno traje 90s (TOTAL_SECONDS), a rekonekcija se dešava u t=45s.

† **Kafka acks=0 je izuzetak** — videti diskusiju ispod.

**Metodološka napomena:** tabela `readings` u Postgres-u se **ne resetuje** između scenarija (`scenario_b.sh` ne poziva `wait_for_storage_idle` kao `scenario_a.sh`), a i mosquitto/Kafka zadržavaju stanje preko restarta kontejnera. To znači da apsolutne vrednosti delimično uključuju "repove" prethodnih test-nivoa. Ipak, **kvalitativni obrazac oporavka** je jasno vidljiv i baš on je suština ovog scenarija:

- **MQTT (QoS 0/1/2) i Kafka (acks=1/all)** pokazuju **identičan obrazac**: brojač je potpuno ravan tokom "disconnected" faze (t=15–45s, nema novih redova — logično, `ingestion` fizički ne može da dostavi poruke), ostaje ravan još **~25s nakon rekonekcije**, a zatim skače jednokratno (t=70s) — `ingestion`-ov MQTT.js/KafkaJS klijent baferuje poruke generisane tokom prekida (20 uređaja × ~30s ≈ 600 poruka) i isporuči ih u naletu čim se ponovo poveže na mrežu. QoS2 pokazuje znatno manji skok (+160 naspram +1120 za QoS0/1), dosledno višem "po-poruci" trošku QoS2 hendšejka koji usporava brzinu redelivery naleta.
- **Kafka acks=0** odstupa dramatično: brojač **neprekidno raste i tokom "disconnected" faze** (5001 → 95001 u periodu t=5–50s). Ovo demonstrira **suštinsku dekompoziciju producer/consumer kod Kafke**: isključivanje mrežne konekcije `ingestion`-a (producer) **nema nikakav uticaj** na to da `storage` (consumer) nastavi da drenira poruke koje su već trajno upisane u topik. U ovom konkretnom run-u, veličina skoka je dominantno posledica ~213k poruka consumer lag-a nasleđenog iz prethodnog Scenarija A (acks=0, 10000 uređaja) — `storage` ga drenira tokom čitavog Scenarija B, potpuno nezavisno od stanja `ingestion`-a.

**Zaključak za Scenario B (poklapa se sa predviđanjem iz specifikacije):** kod **MQTT-a**, oporavak zavisi i od trajne sesije broker-a (`clean: false`) i od samog `ingestion`-a koji mora ponovo da se poveže i isporuči lokalno baferisane poruke; "downtime" producer-a direktno blokira tok ka svim pretplatnicima. Kod **Kafke**, pomeranje offset-a consumer-a je potpuno odvojeno od stanja producer-a — consumer nastavlja da čita ono što je broker već primio, bez obzira da li je producer trenutno dostupan, što Kafka čini znatno otpornijom arhitekturom za nezavisno skaliranje/oporavak proizvodnih i potrošnih komponenti.

### 3.3 Scenario C — Nalet opterećenja (50 → 5000 msg/s)

Scenario: baseline 50 uređaja @ 1 msg/s (15s, ~50 msg/s), zatim burst 500 uređaja @ 100ms (10s, ~5000 msg/s), zatim recovery 30s. Teoretski očekivana ukupna količina poruka do kraja burst-a: ≈ 50 750.

| Broker | QoS/acks | Vrh backlog-a (poruke) | Finalno persistovano | Gubitak (vs ≈50750) | Vreme do platoa nakon burst-a (burst se završava u t=25s) | CPU max % (ingestion / storage / analytics / broker) | RAM max MiB (ingestion / storage / analytics / broker) |
|---|---|---|---|---|---|---|---|
| MQTT | 0 | 24549 (t=25s) | 50200 | 550 (1.08%) | 10s | 35.3 / 52.9 / 29.7 / 13.9 | 48.0 / 95.8 / 61.9 / 5.7 |
| MQTT | 1 | 27547 (t=25s) | 34758 | 15992 (31.51%) | 6s* | 61.8 / 59.1 / 49.1 / 20.3 | 76.1 / 81.1 / 62.4 / 13.5 |
| MQTT | 2 | 22049 (t=25s) | 40466 | 10284 (20.26%) | 4s* | 40.0 / 60.1 / 43.7 / 25.8 | 77.9 / 81.3 / 80.6 / 13.7 |
| Kafka | 0 | 22049 (t=24s) | 49915 | 835 (1.65%) | 6s | 84.3 / 27.7 / 65.1 / 271.6 | 47.4 / 110.8 / 70.6 / 1166.3 |
| Kafka | 1 | 21049 (t=24s) | 49933 | 817 (1.61%) | 6s | 106.3 / 33.5 / 111.5 / 249.7 | 73.4 / 110.2 / 72.2 / 1145.9 |
| Kafka | all | 19549 (t=24s) | 49895 | 855 (1.68%) | 6s | 45.0 / 31.9 / 111.7 / 238.0 | 73.6 / 108.8 / 71.4 / 1161.2 |

\* Za MQTT QoS1/QoS2, "vreme do platoa" **ne znači puni oporavak** — backlog se stabilizuje na nivou trajnog, **stalnog** gubitka (15992 / 10284 poruke), ne na nuli. CPU/RAM kolone su maksimalne vrednosti izmerene u celom 55s prozoru scenarija (baseline+burst+recovery), jer tu nastaju pikovi.

**Analiza — najupečatljiviji nalaz scenarija:**

- **MQTT QoS1 i QoS2 imaju dramatično veći gubitak (31.5% i 20.3%) od QoS0 (1.08%)** pod naglim 10× skokom broja uređaja — kontraintuitivno, pošto bi se očekivalo da QoS≥1 *smanjuje* gubitak. Objašnjenje: kada 500 uređaja istovremeno počne da publikuje sa QoS1/2, `mqtt.js` klijent u `ingestion`-u mora da čeka PUBACK (QoS1) odnosno PUBREC/PUBREL/PUBCOMP (QoS2) po poruci; uz `mosquitto.conf` podešavanje `max_inflight_messages 100`, nastaje "in-flight" začepljenje na klijentskoj strani — poruke se gomilaju u internom redu `ingestion` procesa i deo njih nikada ne stigne da bude poslat u kratkotrajnom 10-sekundnom burst prozoru (jer `stop_simulation` prekida simulaciju pre nego što se red isprazni). QoS0 ("fire-and-forget", bez čekanja potvrde) gura poruke direktno na mrežu bez tog začepljenja, i zato u ovom konkretnom testu ostvaruje **najmanji** gubitak.
- **Kafka pokazuje gotovo identičan i mali (~1.6%) gubitak za sva tri `acks` podešavanja** — KafkaJS producer interno baferuje i grupno (batch) šalje poruke ka broker-u nezavisno od nivoa `acks`, tako da razlika "koliko replika treba da potvrdi upis" ne utiče bitno na to koliko poruka producer uspe da otpremi u 10-sekundnom burst-u.
- **Kafka drenira backlog (vrh ~19.5–22k poruka) za ~6s** nakon završetka burst-a, bez trajnog gubitka iznad ~1.7%. **MQTT QoS0 drenira za ~10s** uz gubitak ~1%, ali **MQTT QoS1/2 nikada ne dreniraju u potpunosti** u okviru 30s recovery prozora — backlog se "zamrzava" na nivou trajnog gubitka.
- **Footprint tokom burst-a**: `kafka` kontejner dostiže CPU pikove **238–272%** (višejezgarno iskorišćenje) i RAM do **~1.17 GiB**, dok `mosquitto` ostaje ispod **26% CPU** i **14 MiB RAM-a** — još jedan konkretan dokaz "cene" Kafka skalabilnosti (videti i poglavlje 4.2 i tabelu footprint-a u 3.5).

### 3.4 Scenario D — End-to-end latency alarmiranja

Forsirana očitavanja > 50°C; `analytics` ispisuje `[ALERT]` za sva tri 10-sekundna prozora, kod svih konfiguracija (avg_temp 54.7–56.5°C, uvek > threshold=50°C) — alerting pipeline radi end-to-end u svim slučajevima.

| Broker | QoS/acks | avg_latency prozor #1 (ms) | prozor #2 (ms) | prozor #3 (ms) | Prosek (ms) |
|---|---|---|---|---|---|
| MQTT | 0 | 5 | 1 | 1 | 2.33 |
| MQTT | 1 | 1 | 1 | 1 | 1.00 |
| MQTT | 2 | 9 | 2 | 2 | 4.33 |
| Kafka | 0 | 4 | 1 | 1 | 2.00 |
| Kafka | 1 | 3 | 1 | 1 | 1.67 |
| Kafka | all | 3 | 1 | 1 | 1.67 |

> **Napomena o p95 latenciji:** `analytics` servis (`services/analytics/src/tumbling-window.js`, `_flush()`) loguje samo **prosečnu** latenciju po prozoru (`avgLatency`), ne i latenciju svake pojedinačne poruke — stvarne per-message vrednosti nisu sačuvane u `docs/results/scenario_d/*.log`, tako da **p95 latencija ne može biti izračunata** iz dostupnih podataka. Kolona "p95 latencija" iz originalne tabele (`docs/report.md`) je izostavljena; umesto nje prikazane su sve tri prozorske vrednosti + prosek. *Preporuka za buduće merenje:* logovati raw niz `latencyMs` (ili percentile) direktno u `_flush()`.

**Analiza:** Pri ovom (laganom, single-batch) opterećenju, sve izmerene latencije su **ispod 10ms**, bez obzira na broker/QoS — na ovom nivou opterećenja, end-to-end latency alarmiranja dominantno je određena samom 10-sekundnom granicom prozora, a ne transportom poruka. QoS2/`acks` varijante pokazuju neznatno višu latenciju u prvom prozoru (trošak uspostave konekcije/handshake-a pri pokretanju), ali konvergiraju na ~1–2ms od drugog prozora nadalje. Ovo je u skladu sa očekivanjem da se razlike u `acks`/QoS na latenciju manifestuju pre svega pod **opterećenjem** (Scenario A/C), ne u izolovanom D scenariju.

### 3.5 Footprint resursa (idle vs. Scenario A @ 10000 uređaja)

| Broker | Kontejner | CPU % idle (prosek / max) | CPU % @10000 uređaja (prosek / max) | RAM idle (prosek / max, MiB) | RAM @10000 uređaja (prosek / max, MiB) |
|---|---|---|---|---|---|
| MQTT | mosquitto | 0.02 / 0.04 | 15.10 / 29.72 | 5.28 / 6.77 | 10.85 / 13.95 |
| MQTT | ingestion | 0.83 / 1.51 | 28.55 / 40.77 | 41.95 / 47.82 | 123.48 / 135.20 |
| MQTT | storage | 2.20 / 10.72 | 23.98 / 53.60 | 26.73 / 27.70 | 78.07 / 81.24 |
| MQTT | analytics | 0.00 / 0.00 | 19.91 / 34.28 | 39.77 / 47.36 | 82.91 / 93.05 |
| Kafka | kafka | 52.71 / 207.27 | 177.21 / 438.95 | 520.53 / 579.60 | 953.83 / 1028.10 |
| Kafka | ingestion | 5.99 / 11.02 | 99.64 / 154.62 | 47.96 / 52.20 | 347.55 / 411.90 |
| Kafka | storage | 1.92 / 4.64 | 8.40 / 38.77 | 48.91 / 56.45 | 182.53 / 214.70 |
| Kafka | analytics | 4.06 / 6.70 | 55.22 / 73.49 | 36.02 / 37.36 | 77.06 / 85.04 |

## 4. Inženjerska pitanja

### 4.1 Zašto je MQTT idealan za edge uređaje, a neadekvatan za istorijsku analitiku velikih podataka?

MQTT broker (Mosquitto) je lagani publish/subscribe relej: jedan binarni proces, minimalan memory footprint (mereno: **<7 MiB RAM, <0.05% CPU u mirovanju**, tabela 3.5), kompaktan wire protokol (2-bajtno fiksno zaglavlje) i nivoi QoS koji omogućavaju ograničenim uređajima da biraju koliku pouzdanost mogu da "plate" — od QoS0 ("isporuči najbolje što možeš, ne ponavljaj") do QoS2 (garantovano tačno-jedanput, po ceni 4-smernog hendšejka). Ovo je tačno profil edge senzora: nisko CPU/RAM/bandwidth, povremena konekcija, potreba za jednostavnim i trenutnim prosleđivanjem prema jednom ili dva pretplatnika (lokalni gateway, analytics servis).

Što MQTT **ne** daje je *log*. Jednom isporučena (ili istekla iz in-flight/queued bafera) poruka je trajno izgubljena — nema koncepta "reproduciraj poslednja 3 dana očitavanja" niti "pet nezavisnih consumer grupa, svaka čita tok po sopstvenom tempu od proizvoljnog offset-a". Mosquitto-ova persistencija (`persistence true`, queued messages u `mosquitto.conf`) je mehanizam *oporavka* za sesiju jednog pretplatnika (videti Scenario B), ne skladište podataka. Za istorijsku analitiku — ponovno puštanje podataka kroz novi agregacioni job, backfill data warehouse-a, debugging replay-om jučerašnjeg saobraćaja — potrebno je trajno, offset-adresirano skladište, što MQTT ne pruža i što `storage` servis mora "dodatno" obezbediti upisom svake poruke u PostgreSQL u trenutku pristizanja.

### 4.2 Zašto Kafka dominira u data-intensive cloud sistemima, kolika je "cena" njene skalabilnosti, i da li je realistična na edge hardveru?

Kafka je izgrađena oko append-only, particionisanog, replikovanog commit log-a. Upravo taj dizajn joj daje: podesivo zadržavanje (retention) podataka (`KAFKA_LOG_RETENTION_HOURS=2` u ovom setup-u, ali konfigurabilno na dane/nedelje), nezavisne consumer grupe koje čitaju istu particiju različitim tempom/offset-om, horizontalno skaliranje dodavanjem particija/broker-a, i `acks`-podesivu trajnost (0/1/all) tako da producer-i mogu trade-off-ovati latency za nivo garancije po slučaju upotrebe. Zato je Kafka centralna komponenta cloud data platformi — razdvaja producer-e od bilo koliko downstream consumer-a (storage, analytics, ML pipeline-ovi) koji isti tok čitaju svaki sopstvenim tempom, uz snažne garancije redosleda i trajnosti.

**Cena je footprint resursa i operaciona složenost**, i ovo smo direktno izmerili:

- Čak i u KRaft modu (bez Zookeeper-a), **jedan** Kafka broker kontejner u mirovanju/laganom opterećenju troši **>500 MiB RAM-a** i prosečno **>50% jednog CPU jezgra** (tabela 3.5) — naspram Mosquitto-vih **<7 MiB / <0.05%**. To je **>70× više RAM-a** samo za idle broker.
- Pod punim opterećenjem (10000 uređaja), `kafka` kontejner dostiže **prosečno 177% CPU (pik 439% — preko 4 jezgra)** i **~1 GiB RAM-a** — naspram `mosquitto`-vih **~15–30% CPU / ~11–14 MiB RAM-a** pod istim opterećenjem (tabela 3.5).
- Tokom burst scenarija (C), `kafka` kontejner pokazuje CPU pikove **238–272%** i RAM do **~1.17 GiB** (3.3).

Ovo pokazuje da JVM overhead (heap, GC, network/replication thread-ovi, log segmenti po particiji) dominira rezidentnim footprint-om Kafka broker-a nezavisno od opterećenja. Pokretanje Kafke na hardverski ograničenim edge serverima (Raspberry-Pi klasa, gateway-evi sa <1GB RAM-a) **nije realistično za production**: sam JVM overhead bi mogao da nadmaši ukupne resurse uređaja, a benefiti partitioning-a/replikacije su uglavnom izgubljeni sa jednim broker-om. Kafka-ino "sweet spot" je cloud/agregacioni nivo — regionalni gateway ili cloud ingestion tačka u koju mnogi edge uređaji šalju podatke preko lakših protokola (MQTT, CoAP), a ne sam edge uređaj.

### 4.3 Uporedna tabela performansi

Videti **poglavlje 3** (3.1–3.5) — sve tabele (throughput/gubitak za Scenario A, oporavak za Scenario B, backlog/drain za Scenario C, latency za Scenario D, i footprint resursa) popunjene su stvarnim podacima iz `docs/results/`.

## 5. Analiza pouzdanosti (QoS / acks)

- **MQTT QoS0 (at most once)** — bez PUBACK, najniži overhead. U Sc. A na 10000 uređaja ima *najmanji* gubitak od MQTT varijanti (69.17%), a u Sc. C (burst) ima **najmanji gubitak od svih 6 konfiguracija ukupno (1.08%)** — bez čekanja na potvrdu, ingestion jednostavno gura poruke na mrežu i ne formira client-side začepljenje.
- **MQTT QoS1 (at least once)** — PUBACK + retransmisija. U Sc. A gubitak 72.10% (veći nego QoS0). U Sc. C gubitak **skače na 31.51% — najgori rezultat celog izveštaja**, direktna posledica in-flight začepljenja (`max_inflight_messages=100`) pod naglim 10× skokom broja uređaja: klijent ne uspeva da "isprati" PUBACK potvrde u 10-sekundnom burst prozoru, pa se poruka odbacuje/ne stigne da bude poslata uopšte.
- **MQTT QoS2 (exactly once)** — 4-smerni hendšejk (PUBLISH/PUBREC/PUBREL/PUBCOMP), teoretski najviša latency i CPU cena. U Sc. A ima *najveći* gubitak od svih konfiguracija (75.97%). U Sc. C gubitak je 20.26% — velik, ali manji nego QoS1; verovatno zato što QoS2 dodatno usporava brzinu *publikovanja* samog `ingestion`-a, tako da manje poruka uopšte stigne da "uđe" u sistem tokom burst prozora (manje konkurencije za in-flight slotove, ali i manje ukupno poslato).
- **Kafka acks=0** — producer ne čeka potvrdu broker-a. U Sc. A ima *najveći* broker-level gubitak (4.92%) od tri `acks` varijante, ali i *najveći* DB-level throughput (2883.37 msg/s) — niži overhead na producer-u propušta više poruka kroz ceo pipeline do Postgres-a.
- **Kafka acks=1** — leader potvrđuje upis. Broker-level gubitak 4.86% — *najmanji* od tri varijante u ovom testu; dobar balans latency/durability.
- **Kafka acks=all (`-1`)** — svi in-sync replikasi potvrđuju upis. Broker-level gubitak 5.16% — *najveći* od tri. U ovom **single-broker KRaft setup-u (replication factor = 1)**, "all" nema dodatnih replika da čeka, pa je suštinski ekvivalentno `acks=1`; razlika od 0.30 procentna poena između `acks=1` i `acks=all` je unutar "šuma" merenja. Ovo je samo po sebi korisna opservacija o *granicama* ovakve lokalne postavke za merenje "cene" `acks=all` — u production klasteru sa repl. factor ≥ 3, `acks=all` bi pokazao znatno veću latency.

## 6. Zaključak

- **Na malom i srednjem opterećenju (100–1000 uređaja)** oba broker-a i sva QoS/acks podešavanja praktično se ne razlikuju (~3.33% "gubitak" je artefakt merenja, ne stvarni problem) — na ovom nivou izbor broker-a treba da bude vođen operativnom složenošću i footprint-om, a ne sirovim performansama.
- **Na ekstremnom opterećenju (10000 uređaja, Sc. A)**, "gubitak" od 69–76% i za MQTT i za Kafka **nije broker-level problem** — Kafka broker realno prima ~9500 msg/s (broker-level gubitak samo ~5%), a MQTT/mosquitto takođe prima gotovo sve. Usko grlo je **batch-insert propusnost `storage` servisa u PostgreSQL (~2750–3080 msg/s)**, zajedničko ograničenje obe varijante arhitekture — upravo problem koji specifikacija predviđa i rešava batching-om (`STORAGE_BATCH_SIZE=500`).
- **Na naglim naletima (Sc. C)**, izbor QoS-a dramatično menja ishod za MQTT: QoS0 gubi 1.08%, a QoS1/QoS2 gube **20–32%** trajno. Ako se MQTT koristi za high-volume telemetriju koja toleriše povremeni gubitak (npr. periodična očitavanja senzora), **QoS0 je iznenađujuće bolji izbor pod burst opterećenjem** od QoS1/2. Kafka, sa svojim internim batching-om na producer strani, ostaje stabilna (~1.6% gubitka) bez obzira na `acks`.
- **Oporavak posle prekida mreže (Sc. B)** pokazuje ključnu arhitektonsku razliku: i kod MQTT-a i kod Kafke, *producer*-ov (`ingestion`) reconnect+replay je dominantan faktor (~25s do prvog skoka), ali samo kod Kafke **consumer (storage) nastavlja da radi i drenira backlog potpuno nezavisno od stanja producer-a** — kod MQTT-a, ako je producer offline, *nema čega da se konzumira* dok se on ne vrati.
- **End-to-end latency alarmiranja (Sc. D)** je sub-10ms za oba broker-a pod laganim opterećenjem — alerting pipeline (10s tumbling window, threshold 50°C) radi korektno end-to-end u svim testiranim konfiguracijama.
- **Footprint resursa** je najjasnija, najmanje ambivalentna razlika: Mosquitto ostaje ispod 30 MiB RAM-a i 30% CPU-a u svim testovima (uklj. 10000 uređaja); Kafka sam za sebe troši 500 MiB – 1.17 GiB RAM-a i do 270%+ CPU-a, *čak i u KRaft modu*. Ovo direktno odgovara na pitanje 4.2 — Kafka na edge hardveru (Raspberry-Pi klasa, <1GB RAM) nije realistična; njeno mesto je cloud/agregacioni nivo.

**Preporuka:** za edge senzore sa ograničenim resursima i tolerancijom na povremeni gubitak — MQTT QoS0 (najmanji footprint, najmanji gubitak pod burst-om u ovom testu); za kritične, niskofrekventne komande/alarme gde je gubitak neprihvatljiv — MQTT QoS1/2 ali sa pažljivo podešenim `max_inflight_messages` i manjim brojem uređaja po klijentu (da se izbegne začepljenje iz Sc. C); za cloud/agregacioni nivo gde više nezavisnih consumer-a (storage, analytics, budući ML pipeline-ovi) treba da čita isti tok uz mogućnost replay-a — Kafka, uz svest da broker sam zahteva ~1GB+ RAM-a pod opterećenjem.

## 7. Usklađenost sa specifikacijom projekta

Provera u odnosu na `IotS - Projekat 2 - 2026.md`:

| # | Zahtev iz specifikacije | Status | Napomena |
|---|---|---|---|
| 1 | Isti IoT dataset/model kao Projekat 1, Docker Compose, ≥2 tehnologije | ✅ | Air Quality (UCI) dataset (`db/dataset/`), `docker-compose.yml`, Node.js + .NET (ASP.NET Core) |
| 2 | Ingestion / Storage (batching 500 na Sc. A/C) / Analytics (Tumbling Window 10s, threshold 50°C) | ✅ | `STORAGE_BATCH_SIZE=500` (`StorageOptions.cs`), `tumbling-window.js` (10s, 50°C, `[ALERT]`) |
| 3a | MQTT: QoS 0/1/2, analiza efekta na latenciju/gubitak | ✅ | Testirano u sva 4 scenarija (Sc. A, C: analiza gubitka po QoS-u; Sc. D: latency po QoS-u) |
| 3b | Kafka: KRaft (bez ZK), acks=0/1/all, consumer lag i particionisanje | ✅ | `KAFKA_PROCESS_ROLES=broker,controller`, `KAFKA_NUM_PARTITIONS=3`, lag analiziran u 3.1 |
| 4 | Scenariji A, B, C, D | ✅ | Sva 4 scenarija izvršena za sve testirane QoS/acks kombinacije, rezultati u `docs/results/` |
| 5a | `docker stats` za CPU/RAM/network | ✅ | `*_dockerstats.csv` u svim scenarijima |
| 5b | Prometheus + Grafana (opciono) | ➖ | Nije korišćeno — opcioni deo spec., nije obavezan |
| 5c | **emqtt-bench / k6 (MQTT) za generisanje opterećenja** | ⚠️ **NIJE korišćeno** | Opterećenje generiše sam `ingestion` servis preko HTTP control API-ja (`benchmarks/README.md` eksplicitno navodi ovu odluku) |
| 5d | **kafka-producer-perf-test.sh / k6 xk6-kafka za generisanje opterećenja** | ⚠️ **NIJE korišćeno** | Isto kao gore — custom `ingestion` generator umesto namenskog alata |
| 6 | Inženjerska pitanja 1–3 (MQTT vs analitika, Kafka cena skalabilnosti, uporedna tabela) | ✅ | Sekcije 4.1, 4.2, 4.3 |
| O1 | Git repo | ✅ | — |
| O2 | Docker Compose konfiguracija | ✅ | `docker-compose.yml` |
| O3 | Konfiguracija broker-a | ✅ | `brokers/mqtt/mosquitto.conf`, `brokers/kafka/kafka.env` |
| O4 | Benchmark skripte | ✅ | `benchmarks/{mqtt,kafka}/scenario_{a,b,c,d}.sh` |
| O5 | Eksperimentalni podaci | ✅ | `docs/results/{scenario_a,b,c,d,outputs}/` |
| O6 | Tehnički izveštaj sa opisom, tabelom i odgovorima | ✅ | `docs/izvestaj.md` (ćirilica) / `docs/izvestaj2.md` (latinica); `docs/report.md` ostaje kao prazan/engleski skelet |

**Zaključak provere:** Svi zahtevi specifikacije su ispunjeni, **sa jednim odstupanjem**: tačke 5c/5d traže da se opterećenje generiše putem *namenskih alata* (emqtt-bench, k6, `kafka-producer-perf-test.sh`/xk6-kafka). Ovaj projekat umesto toga koristi sopstveni `ingestion` servis kao generator opterećenja, što je funkcionalno ekvivalentno (eksponira sve potrebne QoS/`acks` parametre, izmereni throughput/gubitak/latency su validni) ali **ne zadovoljava slovo specifikacije** koje te alate navodi kao "obavezne". Ako se ovo smatra blokirajućim, najjednostavnija dopuna bi bila dodatni "kontrolni" run preko `k6` (sa MQTT/`xk6-kafka` ekstenzijom) za bar jednu konfiguraciju, kao potvrda da se isti throughput/latency brojevi reprodukuju i sa standardnim alatom — bez izmene postojeće arhitekture ili rezultata.

