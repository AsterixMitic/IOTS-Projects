import http from 'k6/http';
import grpc, { StatusOK } from 'k6/net/grpc';
import { check } from 'k6';

import {
  config,
  buildQuery,
  graphqlAggregateBody,
  utcBefore,
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

function scenario(exec, rate) {
  return {
    executor: 'constant-arrival-rate',
    exec,
    rate,
    timeUnit: '1s',
    duration: __ENV.LOADTEST_DURATION || '1m',
    preAllocatedVUs: Number(__ENV.LOADTEST_PRE_ALLOCATED_VUS || 10),
    maxVUs: Number(__ENV.LOADTEST_MAX_VUS || 50),
  };
}

const rate = Number(__ENV.LOADTEST_RATE || 15);
const from = __ENV.LOADTEST_HISTORICAL_FROM || utcBefore(24 * 14);
const to = __ENV.LOADTEST_HISTORICAL_TO || utcBefore(0);
const bucketMinutes = Number(__ENV.LOADTEST_BUCKET_MINUTES || 1440);

export const options = {
  scenarios: {
    rest_historical: scenario('restHistoricalAggregation', Number(__ENV.REST_RATE || rate)),
    grpc_historical: scenario('grpcHistoricalAggregation', Number(__ENV.GRPC_RATE || rate)),
    graphql_historical: scenario('graphqlHistoricalAggregation', Number(__ENV.GRAPHQL_RATE || rate)),
  },
  thresholds: {
    checks: ['rate>0.99'],
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
