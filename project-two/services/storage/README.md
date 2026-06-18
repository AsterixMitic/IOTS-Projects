# Data Storage Service

.NET service that subscribes to MQTT or Kafka events and batches inserts into PostgreSQL.

## Configuration

- `BROKER_MODE=mqtt|kafka`
- `BROKER_URL=mqtt://mosquitto:1883` or `kafka://kafka:9092`
- `MQTT_TOPIC=iot/readings`
- `KAFKA_TOPIC=iot.readings`
- `DATABASE_URL` or `ConnectionStrings__Postgres`
- `BATCH_SIZE=500`
- `FLUSH_INTERVAL_MS=1000`
- `ENABLE_WRITES=true|false`

## Endpoints

- `GET /`
- `GET /health`
- `GET /config`
