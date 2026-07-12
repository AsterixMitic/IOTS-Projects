import http from 'k6/http';
import grpc, { StatusOK } from 'k6/net/grpc';
import { check } from 'k6';

import {
  config,
  buildScenarios,
  grpcCreateRequest,
  graphqlCreateBody,
  randomSensorCode,
  restCreateBody,
  sensorValue,
} from './common.js';

const grpcClient = new grpc.Client();
grpcClient.load([config.grpcProtoDir], 'iot/v1/readings.proto');
let grpcConnected = false;

function loadGrpc() {
  if (!grpcConnected) {
    grpcClient.connect(config.grpcAddr, { plaintext: true });
    grpcConnected = true;
  }
}

// Scenario A - High-Frequency Ingestion. VUS controls the 10/100/500 load level.
export const options = {
  scenarios: buildScenarios({
    rest: 'restIngestion',
    grpc: 'grpcIngestion',
    graphql: 'graphqlIngestion',
  }),
  thresholds: {
    checks: ['rate>0.95'],
  },
};

export function restIngestion() {
  const sensorCode = randomSensorCode();
  const response = http.post(`${config.restBaseUrl}/api/readings`, restCreateBody(sensorCode, sensorValue(sensorCode)), {
    headers: { 'Content-Type': 'application/json' },
  });

  check(response, {
    'REST ingestion accepted': (r) => r.status === 201,
  });
}

export function grpcIngestion() {
  loadGrpc();
  const sensorCode = randomSensorCode();
  const response = grpcClient.invoke('iot.v1.ReadingsService/CreateReading', grpcCreateRequest(sensorCode, sensorValue(sensorCode)));

  check(response, {
    'gRPC ingestion accepted': (r) => r && r.status === StatusOK,
    'gRPC returned id': (r) => r && r.message && r.message.id > 0,
  });
}

export function graphqlIngestion() {
  const sensorCode = randomSensorCode();
  const response = http.post(
    config.graphqlUrl,
    graphqlCreateBody(sensorCode, sensorValue(sensorCode)),
    { headers: { 'Content-Type': 'application/json' } },
  );

  check(response, {
    'GraphQL ingestion accepted': (r) => r.status === 200,
    'GraphQL returned data': (r) => {
      const body = JSON.parse(r.body);
      return body.data && body.data.createReading && body.data.createReading.id > 0;
    },
  });
}
