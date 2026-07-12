// Command sizecheck reports the serialized Protobuf byte size of gRPC responses
// so they can be compared against the JSON payloads of the REST/GraphQL services
// (assignment task 4b: JSON vs binary Protobuf response size).
//
//	go run -C grpc-service-go ./cmd/sizecheck
package main

import (
	"context"
	"fmt"
	"time"

	pb "grpc-service-go/gen/proto/iot/v1"

	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/types/known/timestamppb"
)

func main() {
	conn, err := grpc.NewClient("localhost:50051", grpc.WithTransportCredentials(insecure.NewCredentials()))
	if err != nil {
		panic(err)
	}
	defer conn.Close()
	client := pb.NewReadingsServiceClient(conn)

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	deviceID := int64(1)
	from := timestamppb.New(time.Date(2004, 3, 10, 0, 0, 0, 0, time.UTC))
	to := timestamppb.New(time.Date(2004, 3, 20, 0, 0, 0, 0, time.UTC))

	// Selective monitoring: 10 readings, two sensors.
	listResp, err := client.ListReadings(ctx, &pb.ListReadingsRequest{
		DeviceId:    &deviceID,
		SensorCodes: []string{"temperature", "relative_humidity"},
		Limit:       10,
	})
	if err != nil {
		panic(err)
	}
	fmt.Printf("gRPC ListReadings: %d readings, protobuf bytes = %d\n", len(listResp.Readings), proto.Size(listResp))

	// Heavy querying: daily temperature aggregate over a 10-day window.
	aggResp, err := client.AggregateReadings(ctx, &pb.AggregateReadingsRequest{
		SensorCode:    "temperature",
		BucketMinutes: 1440,
		DeviceId:      &deviceID,
		From:          from,
		To:            to,
	})
	if err != nil {
		panic(err)
	}
	fmt.Printf("gRPC AggregateReadings: %d points, protobuf bytes = %d\n", len(aggResp.Points), proto.Size(aggResp))
}
