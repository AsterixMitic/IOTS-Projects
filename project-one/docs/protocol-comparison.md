# Protocol Comparison — REST vs gRPC vs GraphQL

This document is the qualitative analysis that accompanies the quantitative
results in [`benchmarks.md`](benchmarks.md). All three services expose the same
five operations over the same normalized PostgreSQL schema, so differences come
from the **transport and serialization**, not the business logic.

---

## 1. At a glance

| Aspect | REST (C#/ASP.NET) | gRPC (Go) | GraphQL (Rust) |
|---|---|---|---|
| Transport | HTTP/1.1 | HTTP/2 | HTTP/1.1 |
| Serialization | JSON (text) | Protobuf (binary) | JSON (text) |
| Contract | OpenAPI / Swagger | `.proto` (codegen) | GraphQL SDL / introspection |
| Endpoints | many routes | many methods | single endpoint |
| Response shape | fixed by server | fixed by message | **chosen by client** |
| Over-fetching | likely | message-shaped | avoided |
| Browser-native | yes | no (needs grpc-web) | yes |
| Streaming | no (here) | yes (native) | no (here) |
| Human-readable wire | yes | no | yes |
| Tooling | excellent, ubiquitous | strong, typed stubs | good, growing |

---

## 2. Suitability per IoT scenario

### Scenario A — High-Frequency Ingestion
A device streams many small writes. What matters is per-request overhead.
- **gRPC** is the natural fit: compact binary frames, HTTP/2 multiplexing, one
  persistent connection, typed payloads — least bytes and least parsing per write.
- **REST** is simplest to integrate (any HTTP client) but pays JSON parsing +
  HTTP/1.1 per-request overhead.
- **GraphQL** mutations work but add query-parsing overhead per write; ingestion
  is not where GraphQL shines.

### Scenario B — Selective Monitoring (client wants 2 of N sensors)
A constrained client (poor link) wants a *subset* of fields.
- **GraphQL** wins by design: the client names the exact fields and the server
  returns only those — no server change, no over-fetch. This is the scenario the
  project is built to highlight, and the rewritten Rust service now honors it (a
  `{ id }` query returns only `id`).
- **REST** must expose a purpose-built `sensors=` filter to avoid over-fetching.
- **gRPC** returns the message as shaped; trimming means new request fields or
  new methods.

### Scenario C — Heavy Querying (historical aggregation)
Large range, server-side aggregation (avg/min/max/count per time bucket).
- Here the **database** does the heavy lifting; all three return a compact
  aggregate series. Protocol choice matters less for CPU, more for payload size
  and how naturally the query expresses filters/ranges.
- gRPC's binary payload is smallest; GraphQL lets the client drop unused
  aggregate fields; REST is the most cacheable via plain HTTP semantics.

---

## 3. Payload size — JSON vs binary Protobuf (task 4b)

### Methodology
- **REST / GraphQL (JSON):** UTF-8 byte count of the HTTP response body for a
  fixed query. The Postman collections in each service also log this in the
  Console via `pm.response.responseSize` (see the `[SIZE]` log line in each
  collection's test script).
- **gRPC (Protobuf):** exact serialized size via `proto.Size(response)` from a
  small Go client, [`grpc-service-go/cmd/sizecheck`](../grpc-service-go/cmd/sizecheck/main.go)
  (`go run -C grpc-service-go ./cmd/sizecheck`). This is the true binary body size,
  which Postman cannot show directly for gRPC.
- **Identical logical dataset:** the daily **temperature aggregate** for
  2004-03-10 → 2004-03-20 (each protocol returns the same avg/min/max/count points).

### Results

**Aggregate (daily temperature, ~10–11 points):**

| Protocol | Format | Body bytes | Points | Bytes / point | vs JSON |
|---|---|---:|---:|---:|---:|
| REST | JSON | 1314 | 11 | ~120 | baseline |
| GraphQL | JSON | 1130 | 10 | ~113 | ~0.94× |
| **gRPC** | **Protobuf** | **429** | 11 | **~39** | **~0.33×** |

**List readings (10 readings, `temperature` + `relative_humidity`):**

| Protocol | Format | Body bytes | vs JSON |
|---|---|---:|---:|
| REST | JSON | 1209 | baseline |
| **gRPC** | **Protobuf** | **690** | **~0.57×** |

### Takeaways
- Binary **Protobuf is ~⅓ the size** of the equivalent JSON on the aggregate
  workload (~67% smaller), and ~57% of JSON on the list workload. On a
  low-bandwidth IoT link this is a decisive advantage for gRPC.
- **GraphQL JSON is marginally smaller than REST JSON** here purely because of
  shorter field names (`measuredAt`/`count` vs `bucketStart`/`samples`); its real
  advantage is *dropping fields entirely*, which grows with how much the client
  chooses not to request.
- JSON's cost is the price of being human-readable and universally debuggable.

---

## 4. Developer experience

- **REST (C#/ASP.NET Core):** lowest barrier — controllers, attributes, automatic
  OpenAPI, testable from any browser/curl/Postman. Weakest at expressing "give me
  only these fields."
- **gRPC (Go):** the `.proto` is a single source of truth that generates typed
  server + client stubs; compile-time safety across languages. Cost: codegen
  toolchain, not browser-friendly, binary payloads need tooling (grpcurl) to
  inspect.
- **GraphQL (Rust/async-graphql):** one endpoint, schema introspection, and a
  Playground; clients evolve their queries without server changes. Cost: the
  server must guard against expensive/deep queries, and caching is less trivial
  than plain HTTP GETs.

---

## 5. Recommendation matrix

| Need | Best fit |
|---|---|
| Service-to-service, high throughput, low bandwidth | **gRPC** |
| Public API, broad client base, easy caching/debugging | **REST** |
| Diverse clients each needing different field subsets | **GraphQL** |
| Constrained device pulling a few of many sensors | **GraphQL** |
| Streaming telemetry | **gRPC** |

There is no universal winner: gRPC minimizes bytes and parsing, REST maximizes
reach and simplicity, GraphQL minimizes over-fetching. For an IoT edge-cloud
continuum a common pattern is **gRPC between services** and **REST/GraphQL at the
client edge**.

See [`benchmarks.md`](benchmarks.md) for measured latency, p95, RPS and
CPU/RAM under 10 / 100 / 500 virtual users.
