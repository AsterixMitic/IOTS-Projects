# Benchmark Results

Generated: 2026-07-12 · Dataset: UCI Air Quality (9,357 readings · 105,397 values ·
2004-03-10 → 2005-04-04) · All services and PostgreSQL running under Docker Compose.

These results supersede the earlier placeholder run (which failed because the
database had not been migrated/seeded and the historical queries targeted "now"
instead of the 2004–2005 data). The database was migrated + imported, the k6
scripts rewritten to sweep **10 / 100 / 500 virtual users**, and every k6-inserted
row was cleaned up afterward (final count back to exactly 9,357 readings).

---

## 1. Method

- **Tool:** k6 (`grafana/k6` container), `constant-vus` executor, **12 s** per level.
- **Load levels:** 10, 100, 500 virtual users (assignment task 4a).
- **Isolation:** each protocol was loaded **on its own** (`PROTOCOL=rest|grpc|graphql`)
  so `http_req_duration` / `grpc_req_duration` are cleanly attributable — no
  cross-protocol contention in the latency tables.
- **Metrics:** average latency, p95 latency, requests/second (RPS), and k6 check
  pass rate. RPS = completed iterations per second (1 request per iteration).
- **Scripts:** [`load-tests/k6/`](../load-tests/k6) — `01-ingestion.js`,
  `02-selective-monitoring.js`, `03-historical-aggregation.js`.
- **Raw data:** `load-tests/results/bench-<scenario>-<protocol>-<vus>.json`
  (k6 `--summary-export`).

> **Read latency alongside RPS.** A service that sustains a much higher RPS at a
> given VU level is doing more work, so its higher CPU (below) is expected. The
> differences below are dominated by each service's **SQL query + DB connection
> pool**, not by the wire format — see [§5 Analysis](#5-analysis).

---

## 2. Scenario A — High-Frequency Ingestion (writes)

Each virtual user issues create-reading calls as fast as possible.

| Protocol | VUs | Avg latency (ms) | p95 (ms) | RPS | Checks pass |
|---|---:|---:|---:|---:|---:|
| REST | 10 | 61.1 | 90.2 | 161.9 | 100% |
| REST | 100 | 742.7 | 2275.5 | 130.5 | 96.73% |
| REST | 500 | 3755.2 | 6100.0 | 120.0 | 92.57% |
| gRPC | 10 | 17.8 | 25.5 | 534.1 | 100% |
| gRPC | 100 | 146.8 | 234.7 | 667.4 | 100% |
| gRPC | 500 | 567.6 | 725.5 | 837.7 | 100% |
| GraphQL | 10 | 16.5 | 21.8 | 581.5 | 100% |
| GraphQL | 100 | 127.1 | 175.1 | 774.4 | 100% |
| GraphQL | 500 | 585.5 | 906.1 | 789.6 | 94.87% |

**Observation:** gRPC and GraphQL sustain **~5–7× the write throughput** of REST.
The REST (EF Core) path does extra round-trips per insert (device-exists check,
sensor-code lookup, two `SaveChanges`), so it saturates earlier and starts failing
checks under 100+ VUs.

---

## 3. Scenario B — Selective Monitoring (read 2 of many sensors)

| Protocol | VUs | Avg latency (ms) | p95 (ms) | RPS | Checks pass |
|---|---:|---:|---:|---:|---:|
| REST | 10 | 18.0 | 26.5 | 508.2 | 100% |
| REST | 100 | 332.5 | 1412.7 | 288.6 | 98.65% |
| REST | 500 | 1569.0 | 2564.3 | 293.9 | 97.88% |
| gRPC | 10 | 224.0 | 273.1 | 44.0 | 100% |
| gRPC | 100 | 1966.9 | 2283.2 | 46.6 | 100% |
| gRPC | 500 | 8377.2 | 11275.3 | 49.6 | 100% |
| GraphQL | 10 | 538.0 | 646.1 | 18.3 | 100% |
| GraphQL | 100 | 4338.4 | 5291.3 | 20.2 | 100% |
| GraphQL | 500 | 11716.9 | 28503.6 | 28.3 | 63.72% |

**Observation:** here the order **reverses** — REST is fastest. This is a
query/pool effect, not a protocol effect:

- REST (Npgsql, default pool ~100) parallelizes its two indexed queries well.
- gRPC (Go pgxpool, small default pool) runs one `jsonb_object_agg` GROUP BY per
  request and queues on the pool → low RPS.
- GraphQL `listReadings` currently sorts by `created_at` (no index) over the full
  join, the heaviest plan of the three → lowest RPS, and check failures at 500 VUs.

This is the clearest illustration that **implementation/tuning dominates transport**
at these scales (see [§5](#5-analysis) for the fix that would rebalance this).

---

## 4. Scenario C — Heavy Querying (historical aggregation)

| Protocol | VUs | Avg latency (ms) | p95 (ms) | RPS | Checks pass |
|---|---:|---:|---:|---:|---:|
| REST | 10 | 96.2 | 153.1 | 91.1 | 100% |
| REST | 100 | 1087.9 | 2029.5 | 86.1 | 81.41% |
| REST | 500 | 4358.2 | 9478.1 | 95.9 | 74.37% |
| gRPC | 10 | 148.3 | 214.6 | 62.4 | 100% |
| gRPC | 100 | 1006.4 | 1204.2 | 91.2 | 100% |
| gRPC | 500 | 5034.9 | 6349.0 | 82.0 | 100% |
| GraphQL | 10 | 131.7 | 202.7 | 67.9 | 100% |
| GraphQL | 100 | 1131.8 | 2834.2 | 84.2 | 100% |
| GraphQL | 500 | 4250.2 | 5493.6 | 98.5 | 100% |

**Observation:** with the database doing the aggregation, the three protocols are
much closer. gRPC and GraphQL hold 100% checks under load; REST's aggregate
endpoint sheds ~19–26% of checks at 100–500 VUs (request errors under saturation).

---

## 5. Resource usage — CPU & RAM (task 4c)

Captured with `docker stats` while all three services were driven concurrently by
the selective-monitoring load at **100 VUs each** (peak values across the run;
`docker stats` reports CPU as % of a single core, so >100% = multiple cores).

| Container | Peak CPU | RAM | Notes |
|---|---:|---:|---|
| `postgres` | ~1300–1560% | ~260 MiB | **the real bottleneck** — 13–15 cores |
| `rest-service-csharp` | ~225% | ~250 MiB | highest RPS → highest app CPU; largest footprint |
| `grpc-service-go` | ~14% | ~116 MiB | low CPU (pool-bound, low RPS) |
| `graphql-service-rust` | ~4% | ~43 MiB | **leanest** — Rust footprint |
| `prometheus` | ~0% | ~79 MiB | idle scraper |

**Serialization cost:** at this scale, per-request CPU on the app services is
dwarfed by PostgreSQL. Memory footprint follows the expected runtime ordering —
**Rust (43 MiB) < Go (116 MiB) < .NET (250 MiB)**. REST's higher CPU reflects the
~6–14× higher RPS it sustained during this capture, not per-message inefficiency.

Live dashboards: Grafana `http://localhost:3000` (admin/admin), Prometheus
`http://localhost:9090`.

---

## 6. Payload size — JSON vs Protobuf (task 4b)

Same logical dataset (daily temperature aggregate, 2004-03-10 → 2004-03-20):

| Protocol | Format | Body bytes | Points | Bytes/point |
|---|---|---:|---:|---:|
| REST | JSON | 1314 | 11 | ~120 |
| GraphQL | JSON | 1130 | 10 | ~113 |
| **gRPC** | **Protobuf** | **429** | 11 | **~39** |

Binary Protobuf is **~⅓ the size** of the equivalent JSON. Full methodology and a
second (list-readings) measurement are in
[`protocol-comparison.md`](protocol-comparison.md).

---

## 7. Analysis & caveats

- **Transport ≠ the dominant variable here.** JSON-vs-Protobuf and HTTP/1.1-vs-HTTP/2
  clearly affect payload size (§6) and are visible in the fast ingestion path, but
  the read scenarios are governed by each service's SQL plan and **DB connection
  pool size**. The comparison is therefore as much about the three *implementations*
  as about the three *protocols* — which is itself a useful lesson.
- **Concrete rebalancing levers:** raise the Go `pgxpool` and Rust `sqlx` pool
  sizes; add a `readings(created_at)` index or change GraphQL `listReadings` to
  order by the already-indexed `recorded_at`; give GraphQL a sensor-code filter so
  Scenario B fetches less. With those, the read gap would shrink markedly.
- **Failures under load** (checks < 100%) are genuine request errors when a service
  saturates (REST aggregate at 100–500 VUs; GraphQL selective at 500 VUs), not
  script bugs — the same scripts hit 100% at 10 VUs.
- **Numbers are indicative, not absolute:** single machine, Docker Desktop, 12 s
  samples. Re-running will vary with host load.

---

## 8. Reproduce

```bash
# 1. bring the stack up and load data (once)
cd project-one
docker compose up -d
bash db/run_migrations.sh
bash db/import_via_copy.sh db/dataset/AirQualityUCI.csv

# 2. one scenario at one level (example: selective monitoring, gRPC, 100 VUs)
docker run --rm -v "$(pwd):/work" -w /work \
  -e VUS=100 -e DURATION=12s -e PROTOCOL=grpc \
  -e GRPC_PROTO_DIR=/work/grpc-service-go/proto \
  grafana/k6 run --summary-export=/work/load-tests/results/out.json \
  load-tests/k6/02-selective-monitoring.js

# PROTOCOL=all runs the three concurrently; VUS sweeps 10/100/500.
# gRPC Protobuf payload size:
go run -C grpc-service-go ./cmd/sizecheck
```
