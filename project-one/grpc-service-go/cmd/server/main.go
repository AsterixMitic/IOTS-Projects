package main

import (
"context"
"encoding/json"
"fmt"
"log"
"net"
"os"
"os/signal"
"syscall"
"time"

pb "grpc-service-go/gen/proto/iot/v1"
"grpc-service-go/internal/application"
"grpc-service-go/internal/domain"
"grpc-service-go/internal/infra"

"github.com/jackc/pgx/v5/pgxpool"
"google.golang.org/grpc"
"google.golang.org/protobuf/types/known/emptypb"
"google.golang.org/protobuf/types/known/timestamppb"
)

type grpcServer struct {
svc *application.ReadingsService
pb.UnimplementedReadingsServiceServer
}

func (s *grpcServer) ListSensorTypes(ctx context.Context, _ *emptypb.Empty) (*pb.ListSensorTypesResponse, error) {
items, err := s.svc.ListSensorTypes(ctx)
if err != nil {
return nil, err
}
resp := &pb.ListSensorTypesResponse{}
for _, it := range items {
resp.SensorTypes = append(resp.SensorTypes, &pb.SensorType{
Id:    it.ID,
Code:  it.Code,
Label: it.Label,
Unit:  it.Unit,
})
}
return resp, nil
}

func (s *grpcServer) ListReadings(ctx context.Context, req *pb.ListReadingsRequest) (*pb.ListReadingsResponse, error) {
var filter applicationFilter
if req == nil {
filter.limit = 100
} else {
if d := req.GetDeviceId(); d > 0 {
v := d
filter.deviceID = &v
}
if req.From != nil {
t := req.From.AsTime().UTC()
filter.from = &t
}
if req.To != nil {
t := req.To.AsTime().UTC()
filter.to = &t
}
filter.limit = int(req.GetLimit())
filter.offset = int(req.GetOffset())
for _, sc := range req.GetSensorCodes() {
filter.sensorCodes = append(filter.sensorCodes, sc)
}
}

// map to domain filter
df := mapToDomainFilter(filter)

readings, err := s.svc.ListReadings(ctx, df)
if err != nil {
return nil, err
}
resp := &pb.ListReadingsResponse{}
for _, r := range readings {
item := &pb.ReadingSummary{
Id:         r.ID,
DeviceId:   r.DeviceID,
RecordedAt: timestamppb.New(r.RecordedAt),
Values:     map[string]float64{},
}
for k, v := range r.Values {
item.Values[k] = v
}
resp.Readings = append(resp.Readings, item)
}
return resp, nil
}

func (s *grpcServer) GetReading(ctx context.Context, req *pb.GetReadingRequest) (*pb.GetReadingResponse, error) {
if req == nil || req.GetId() <= 0 {
return nil, fmt.Errorf("invalid id")
}
reading, err := s.svc.GetReadingByID(ctx, req.GetId())
if err != nil {
return nil, err
}
ret := &pb.GetReadingResponse{Reading: &pb.ReadingDetail{}}
ret.Reading.Id = reading.ID
ret.Reading.DeviceId = reading.DeviceID
ret.Reading.RecordedAt = timestamppb.New(reading.RecordedAt)
ret.Reading.Notes = reading.Notes
ret.Reading.Values = map[string]float64{}
for k, v := range reading.Values {
ret.Reading.Values[k] = v
}
return ret, nil
}

func (s *grpcServer) CreateReading(ctx context.Context, req *pb.CreateReadingRequest) (*pb.CreateReadingResponse, error) {
if req == nil {
return nil, fmt.Errorf("empty request")
}
input := domain.CreateReadingInput{}
input.DeviceID = req.GetDeviceId()
if req.RecordedAt != nil {
input.RecordedAt = req.RecordedAt.AsTime().UTC()
} else {
input.RecordedAt = time.Now().UTC()
}
input.Notes = req.GetNotes()
input.Values = map[string]float64{}
for k, v := range req.GetValues() {
input.Values[k] = v
}
id, err := s.svc.CreateReading(ctx, input)
if err != nil {
return nil, err
}
return &pb.CreateReadingResponse{Id: id}, nil
}

func (s *grpcServer) AggregateReadings(ctx context.Context, req *pb.AggregateReadingsRequest) (*pb.AggregateReadingsResponse, error) {
if req == nil || req.GetSensorCode() == "" {
return nil, fmt.Errorf("sensor_code required")
}
filter := domain.AggregateFilter{}
filter.SensorCode = req.GetSensorCode()
filter.BucketMinutes = int32(req.GetBucketMinutes())
if req.From != nil {
f := req.From.AsTime().UTC()
filter.From = &f
}
if req.To != nil {
t := req.To.AsTime().UTC()
filter.To = &t
}
if req.GetDeviceId() > 0 {
v := req.GetDeviceId()
filter.DeviceID = &v
}
points, err := s.svc.AggregateReadings(ctx, filter)
if err != nil {
return nil, err
}
resp := &pb.AggregateReadingsResponse{}
for _, p := range points {
resp.Points = append(resp.Points, &pb.AggregatePoint{
BucketStart: timestamppb.New(p.BucketStart),
AvgValue:    p.AvgValue,
MinValue:    p.MinValue,
MaxValue:    p.MaxValue,
Samples:     p.Samples,
})
}
return resp, nil
}

// -- small DTO adapters to avoid repeating code in generated handlers

type applicationFilter struct {
deviceID    *int64
from        *time.Time
to          *time.Time
sensorCodes []string
limit       int
offset      int
}

func mapToDomainFilter(f applicationFilter) domain.ReadingsFilter {
return domain.ReadingsFilter{
DeviceID:    f.deviceID,
From:        f.from,
To:          f.to,
SensorCodes: f.sensorCodes,
Limit:       int32(f.limit),
Offset:      int32(f.offset),
}
}

func main() {
ctx := context.Background()
dsn := os.Getenv("DATABASE_URL")
if dsn == "" {
user := os.Getenv("POSTGRES_USER")
pass := os.Getenv("POSTGRES_PASSWORD")
db := os.Getenv("POSTGRES_DB")
if user == "" {
user = "iot_user"
}
if pass == "" {
pass = "iot_password"
}
if db == "" {
db = "iot_project"
}
dsn = fmt.Sprintf("postgres://%s:%s@postgres:5432/%s?sslmode=disable", user, pass, db)
}

pool, err := pgxpool.New(ctx, dsn)
if err != nil {
log.Fatalf("failed to connect to db: %v", err)
}
defer pool.Close()

repo := infra.NewPgxRepository(pool)
svc := application.NewReadingsService(repo)

grpcSrv := grpc.NewServer()
server := &grpcServer{svc: svc}
pb.RegisterReadingsServiceServer(grpcSrv, server)

port := os.Getenv("GRPC_PORT")
if port == "" {
port = "50051"
}
ln, err := net.Listen("tcp", ":"+port)
if err != nil {
log.Fatalf("failed to listen on %s: %v", port, err)
}

// start server
go func() {
log.Printf("gRPC listening on %s", port)
if err := grpcSrv.Serve(ln); err != nil {
log.Fatalf("gRPC server failed: %v", err)
}
}()

// wait for signal
stop := make(chan os.Signal, 1)
signal.Notify(stop, os.Interrupt, syscall.SIGTERM)
<-stop

log.Printf("shutting down gRPC server")
grpcSrv.GracefulStop()

// small health check dump of counts (best-effort)
var counts struct{Readings, Values int64}
if err := pool.QueryRow(ctx, "SELECT COUNT(*) FROM readings").Scan(&counts.Readings); err == nil {
_ = pool.QueryRow(ctx, "SELECT COUNT(*) FROM reading_values").Scan(&counts.Values)
b, _ := json.Marshal(counts)
log.Printf("db counts: %s", string(b))
}
}
