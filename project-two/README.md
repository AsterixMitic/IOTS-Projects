# Project two - MQTT vs Kafka IoT microservices

This project will compare two event-driven IoT stacks with the same business logic and the same PostgreSQL schema:

- **MQTT** using **Mosquitto**
- **Kafka** using **Apache Kafka in KRaft mode**

The goal is to keep the application logic identical and change only the broker layer so the results stay comparable.

## Service split

We will build **3 logical services**:

| Service | Technology | Responsibility |
|---|---|---|
| Data Ingestion Service | Node.js | Simulate devices and publish sensor events |
| Data Storage Service | .NET | Consume events and persist them to PostgreSQL |
| Analytics Service | Node.js | Consume events and compute 10-second tumbling window alerts |

Each service will support **both brokers** through configuration:

- MQTT mode
- Kafka mode

That means the codebase stays at 3 services, while experiments can run either broker variant.

## Repository layout

```text
project-two/
├── README.md
├── docker-compose.yml
├── .env
├── .env.example
├── db/
│   ├── README.md
│   ├── 0001_schema_and_staging.up.sql
│   ├── 0002_transform.up.sql
│   └── dataset/
├── broker/
│   └── mqtt/
│       └── mosquitto.conf
├── services/
│   ├── ingestion/
│   ├── storage/
│   └── analytics/
├── shared/
│   ├── contracts/
│   └── sql/
├── scripts/
│   └── benchmarks/
└── docs/
    └── results/
```

## Implementation plan

### 1. Shared event contract

Define one canonical IoT reading model with:

- `device_id`
- `timestamp`
- temperature
- humidity
- pressure
- any extra sensor fields needed for the report

Both brokers will transport the same payload shape.

### 2. Data Ingestion Service

This service will:

- simulate 100 / 1000 / 10000 devices
- generate burst traffic for Scenario C
- publish to MQTT or Kafka based on config
- expose knobs for QoS / `acks` testing

### 3. Data Storage Service

This service will:

- subscribe to the active broker
- batch writes to PostgreSQL
- support the Scenario A/C optimization where DB I/O is reduced
- keep the table indexed for `device_id` + `timestamp`

### 4. Analytics Service

This service will:

- consume the same stream independently
- run a 10-second tumbling window
- compute averages and thresholds
- log alerts for end-to-end latency measurement

### 5. Broker-specific configuration

MQTT:

- Mosquitto
- QoS 0 / 1 / 2

Kafka:

- Apache Kafka
- KRaft mode
- `acks=0 / 1 / all`
- consumer lag measurements

### 6. Benchmarks and experiments

We will collect data for:

- throughput
- p95 latency
- loss rate
- recovery time
- consumer lag
- CPU/RAM footprint

Scenarios:

- Massive ingestion
- Network disconnect
- Burst load
- Real-time alerting

## Build order

1. Create the shared contract and DB schema.
2. Implement the storage service in .NET.
3. Implement ingestion in Node.js.
4. Implement analytics in Node.js.
5. Wire MQTT and Kafka adapters.
6. Add benchmark scripts.
7. Run scenarios and record results.

## Notes

- Keep MQTT and Kafka behavior as close as possible at the business level.
- Do not duplicate the whole application for each broker.
- Keep implementation details inside broker adapters so the comparison remains fair.
