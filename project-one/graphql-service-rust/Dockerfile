FROM rust:latest as builder

WORKDIR /app

RUN apt-get update && apt-get install -y libpq-dev pkg-config && rm -rf /var/lib/apt/lists/*

COPY Cargo.toml ./
COPY src ./src

RUN cargo build --release

FROM debian:bookworm-slim

RUN apt-get update && apt-get install -y libpq5 ca-certificates && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=builder /app/target/release/graphql_service /app/graphql_service

ENV RUST_LOG=info
ENV DATABASE_URL=postgresql://root:root@postgres:5432/iot_db

EXPOSE 8000

CMD ["./graphql_service"]
