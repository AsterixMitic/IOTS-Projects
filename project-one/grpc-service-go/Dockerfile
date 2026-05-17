# Build stage
FROM golang:1.22-alpine AS builder

RUN apk add --no-cache git ca-certificates
WORKDIR /src

COPY go.mod ./

COPY . .
RUN CGO_ENABLED=0 GOOS=linux GOFLAGS="-mod=mod" go build -o /grpc-server ./cmd/server

# Final image
FROM alpine:3.18
RUN apk add --no-cache ca-certificates
COPY --from=builder /grpc-server /usr/local/bin/grpc-server
EXPOSE 50051
ENTRYPOINT ["/usr/local/bin/grpc-server"]
