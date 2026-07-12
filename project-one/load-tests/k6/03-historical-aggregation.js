import http from 'k6/http';
import grpc, { StatusOK } from 'k6/net/grpc';
import { check } from 'k6';

import {
  config,
  buildScenarios,
  buildQuery,
  graphqlAggregateBody,
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

const from = config.historicalFrom;
const to = config.historicalTo;
const bucketMinutes = Number(__ENV.LOADTEST_BUCKET_MINUTES || 1440);

// Scenario C - Heavy Querying (aggregation). VUS controls the 10/100/500 load level.
export const options = {
  scenarios: buildScenarios({
    rest: 'restHistoricalAggregation',
    grpc: 'grpcHistoricalAggregation',
    graphql: 'graphqlHistoricalAggregation',
  }),
  thresholds: {
    checks: ['rate>0.95'],
  },
};

export function restHistoricalAggregation() {
  const query = buildQuery({
    sensorCode: config.historicalSensorCode,
    bucketMinutes,
    deviceId: config.restDeviceId,
    from,
    to,
  });
  const response = http.get(`${config.restBaseUrl}/api/readings/aggregate?${query}`);

  check(response, {
    'REST historical aggregation accepted': (r) => r.status === 200,
    'REST returned aggregate array': (r) => Array.isArray(JSON.parse(r.body)),
  });
}

export function grpcHistoricalAggregation() {
  loadGrpc();
  const response = grpcClient.invoke('iot.v1.ReadingsService/AggregateReadings', {
    sensorCode: config.historicalSensorCode,
    bucketMinutes,
    deviceId: config.grpcDeviceId,
    from,
    to,
  });

  check(response, {
    'gRPC historical aggregation accepted': (r) => r && r.status === StatusOK,
    'gRPC returned aggregate array': (r) => r && r.message && Array.isArray(r.message.points),
  });
}

export function graphqlHistoricalAggregation() {
  const response = http.post(
    config.graphqlUrl,
    graphqlAggregateBody(config.historicalSensorCode, from, to),
    { headers: { 'Content-Type': 'application/json' } },
  );

  check(response, {
    'GraphQL historical aggregation accepted': (r) => r.status === 200,
    'GraphQL returned aggregate array': (r) => {
      const body = JSON.parse(r.body);
      return body.data && Array.isArray(body.data.aggregateReadings);
    },
  });
}
