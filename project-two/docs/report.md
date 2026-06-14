# Technical Report — MQTT vs. Kafka IoT Microservices

> Status: skeleton. Fill in the measured tables once `benchmarks/{mqtt,kafka}/scenario_*.sh`
> have been run and their output is collected under `docs/results/`.

## 1. System overview

Three services, each broker-agnostic via `BROKER_MODE=mqtt|kafka`:

| Service | Stack | Role |
|---|---|---|
| `ingestion` | Node.js | Simulates N devices, publishes Air Quality sensor readings to the active broker. QoS (MQTT) / `acks` (Kafka) are configurable via env vars. |
| `storage` | .NET (ASP.NET Core) | Subscribes to the broker, batches inserts (`STORAGE_BATCH_SIZE`, default 500) into PostgreSQL. |
| `analytics` | Node.js | Subscribes independently, runs a 10s tumbling window over `temperature`, logs `[ALERT]` when the window average exceeds `ALERT_THRESHOLD` (default 50°C), and tracks per-message latency. |

Brokers:
- **Mosquitto** (MQTT) — QoS 0/1/2, persistent sessions (`clean: false`) on the analytics consumer.
- **Apache Kafka** (KRaft mode, no Zookeeper) — `acks=0/1/all`, 3 partitions per topic, consumer groups `project-two-storage` and `analytics-group`.

## 2. Experimental scenarios

| Scenario | Description | Script |
|---|---|---|
| A — Massive Sensor Ingestion | 100 / 1000 / 10000 simulated devices @ 1 msg/s | `benchmarks/{broker}/scenario_a.sh` |
| B — Edge Connectivity Failures | 30s `docker network disconnect` on `ingestion`, observe recovery | `benchmarks/{broker}/scenario_b.sh` |
| C — Burst Event Load | ~50 msg/s → ~5000 msg/s burst → recovery | `benchmarks/{broker}/scenario_c.sh` |
| D — Real-Time Alerting | Forced >50°C readings, measure generation→alert latency | `benchmarks/{broker}/scenario_d.sh` |

## 3. Comparative performance table

Fill in after running the benchmark sweep (`benchmarks/README.md`).

### Scenario A — Throughput & loss

`scenario_a.sh` drains any leftover backlog (`wait_for_storage_idle`) before
each device-count tier, so each row reflects only that tier's run.

MQTT (single DB-based measurement — Mosquitto has no offset/log concept):

| QoS | Devices | Expected msgs | Received (DB) | Loss % | Throughput (msg/s) |
|---|---|---|---|---|---|
| 0 | 100 | | | | |
| 1 | 100 | | | | |
| 2 | 100 | | | | |
| 0 | 1000 | | | | |
| 1 | 1000 | | | | |
| 2 | 1000 | | | | |
| 0 | 10000 | | | | |
| 1 | 10000 | | | | |
| 2 | 10000 | | | | |

Kafka — broker-level (topic log-end-offset delta, i.e. messages Kafka durably
appended regardless of consumer speed) vs. storage-level (rows persisted to
Postgres within the run + 5s, plus end-of-run consumer lag):

| acks | Devices | Expected msgs | Broker received | Broker loss % | Broker throughput (msg/s) | Persisted (DB) | Persisted throughput (msg/s) | Storage lag at end |
|---|---|---|---|---|---|---|---|---|
| 0 | 100 | | | | | | | |
| 1 | 100 | | | | | | | |
| all | 100 | | | | | | | |
| 0 | 1000 | | | | | | | |
| 1 | 1000 | | | | | | | |
| all | 1000 | | | | | | | |
| 0 | 10000 | | | | | | | |
| 1 | 10000 | | | | | | | |
| all | 10000 | | | | | | | |

> For Kafka, "broker loss %" measures actual message loss at the broker
> (expected to be ~0% for `acks=1`/`acks=all`, possibly >0% for `acks=0`
> under heavy load). A large gap between "broker received" and "persisted
> (DB)" at 10000 devices is **not** broker loss — it's the storage service's
> batch-insert throughput becoming the bottleneck, as anticipated by the
> assignment brief (batching / disabling writes during Scenarios A/C).

### Scenario B — Recovery after 30s network outage

| Broker | QoS / acks | Rows during outage | Rows recovered (60s after reconnect) | Recovery time (s) | Notes |
|---|---|---|---|---|---|
| MQTT | 1 | | | | persistent session redelivery |
| Kafka | 1 | | | | producer resumes; consumer offset unaffected |

### Scenario C — Burst load (50 → 5000 msg/s)

| Broker | QoS / acks | Peak backlog (rows) | Time to drain backlog (s) | CPU during burst | RAM during burst |
|---|---|---|---|---|---|
| MQTT | 1 | | | | |
| Kafka | 1 | | | | |

### Scenario D — End-to-end alerting latency

| Broker | QoS / acks | avg_latency (ms) | p95 latency (ms) |
|---|---|---|---|
| MQTT | 0 | | |
| MQTT | 1 | | |
| MQTT | 2 | | |
| Kafka | 0 | | |
| Kafka | 1 | | |
| Kafka | all | | |

### Resource footprint (idle vs. Scenario A @ 10000 devices)

| Broker | Container | CPU % idle | CPU % loaded | RAM idle | RAM loaded |
|---|---|---|---|---|---|
| MQTT | mosquitto | | | | |
| MQTT | ingestion | | | | |
| MQTT | storage | | | | |
| MQTT | analytics | | | | |
| Kafka | kafka | | | | |
| Kafka | ingestion | | | | |
| Kafka | storage | | | | |
| Kafka | analytics | | | | |

## 4. Engineering questions

### 4.1 Why is MQTT ideal for edge devices but inadequate for historical analytics on large datasets?

MQTT's broker (Mosquitto) is a lightweight publish/subscribe relay: a single
binary, minimal memory footprint, a tiny wire protocol (2-byte fixed header),
and QoS levels that let constrained devices choose how much reliability they
can afford (QoS 0 for "best effort, don't bother retransmitting" up to QoS 2
for guaranteed exactly-once delivery at the cost of a 4-way handshake). This
is exactly the profile of an edge sensor: low CPU/RAM/bandwidth, intermittent
connectivity, and a need for simple, immediate fan-out to one or two
subscribers (a local gateway, an analytics service).

What MQTT does **not** give you is a *log*. Once a message is delivered (or
expires from the in-flight/queued buffers), it is gone — there is no notion
of "replay the last 3 days of readings" or "let five independent consumer
groups each read the stream at their own pace from arbitrary offsets".
Mosquitto's persistence (`persistence true`, queued messages) is a *recovery*
mechanism for a single subscriber's session, not a data store. For historical
analytics — replaying data into a new aggregation job, backfilling a data
warehouse, debugging by re-running yesterday's traffic — you need durable,
offset-addressable storage, which is precisely what MQTT does not provide and
what the storage service has to bolt on by writing every message into
PostgreSQL as it arrives.

### 4.2 Why does Kafka dominate data-intensive cloud systems, what does its scalability cost, and is it realistic on edge hardware?

Kafka is built around an append-only, partitioned, replicated commit log.
That single design choice gives it: configurable retention (replay
yesterday's data, or weeks of it), independent consumer groups reading the
same partition at different offsets/speeds, horizontal scaling by adding
partitions/brokers, and `acks`-tunable durability (0/1/all) so producers can
trade latency for guarantee level per use case. This is why it sits at the
center of cloud data platforms — it decouples producers from however many
downstream consumers (storage, analytics, ML pipelines) need the same stream,
each at their own pace, with strong ordering and durability guarantees.

The cost is resource footprint and operational complexity. Even in KRaft mode
(no separate ZooKeeper), a single Kafka broker container needs a JVM with a
meaningful heap, disk space for log segments per partition, and CPU for
network/replication threads — multiple times the footprint of Mosquitto for
an idle broker (see the resource footprint table above for measured numbers).
Running it on hardware-constrained edge servers (Raspberry-Pi class devices,
single-core gateways with <1GB RAM) is not realistic for production: the JVM
overhead alone can dominate the device's resources, and the benefits of
partitioning/replication are largely wasted with a single broker. Kafka's
sweet spot is the cloud/aggregation tier — a regional gateway or cloud
ingestion point that many edge devices feed into via lighter protocols (MQTT,
CoAP), not the edge device itself.

### 4.3 Comparative performance table

See section 3 above — populate from `docs/results/`.

## 5. Reliability analysis (QoS / acks)

- **MQTT QoS 0** (at most once): no PUBACK, lowest latency, message loss
  possible under broker/network pressure — expected to show the highest
  Scenario A throughput but non-zero loss % at 10000 devices.
- **MQTT QoS 1** (at least once): PUBACK required, retransmission on timeout
  — possible duplicates, near-zero loss, moderate latency increase.
- **MQTT QoS 2** (exactly once): 4-way handshake (PUBREC/PUBREL/PUBCOMP) —
  highest latency and CPU cost, no loss/duplicates.
- **Kafka acks=0**: producer doesn't wait for any broker ack — lowest
  latency, risk of loss if the leader fails before the write is flushed.
- **Kafka acks=1**: leader write acknowledged — balance of latency and
  durability (default used across most scenarios here).
- **Kafka acks=all**: all in-sync replicas must ack — highest durability,
  highest produce latency (more relevant with replication factor > 1; with
  this single-broker KRaft setup the difference vs. `acks=1` is expected to
  be small, which is itself a useful observation about the limits of this
  local setup for measuring `acks=all` overhead).

## 6. Conclusion

Fill in once the benchmark sweep is complete — summarize which broker fit
which scenario best and tie back to questions 4.1–4.2.
