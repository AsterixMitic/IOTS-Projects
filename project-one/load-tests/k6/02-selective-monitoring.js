import http from 'k6/http';
import grpc, { StatusOK } from 'k6/net/grpc';
import { check } from 'k6';

import {
  config,
  buildQuery,
  graphqlListReadingsBody,
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

const rate = Number(__ENV.LOADTEST_RATE || 25);
const sensors = config.selectiveSensors.join(',');
const offsetStep = Number(__ENV.LOADTEST_OFFSET_STEP || 25);
const maxOffset = Number(__ENV.LOADTEST_MAX_OFFSET || 100);

export const options = {
  scenarios: {
    rest_selective: scenario('restSelectiveMonitoring', Number(__ENV.REST_RATE || rate)),
    grpc_selective: scenario('grpcSelectiveMonitoring', Number(__ENV.GRPC_RATE || rate)),
    graphql_selective: scenario('graphqlSelectiveMonitoring', Number(__ENV.GRAPHQL_RATE || rate)),
  },
  thresholds: {
    checks: ['rate>0.99'],
  },
};

function nextOffset() {
  return (Math.floor(Math.random() * (maxOffset / offsetStep + 1)) * offsetStep);
}

export function restSelectiveMonitoring() {
  const query = buildQuery({
    deviceId: config.restDeviceId,
    sensors,
    limit: 25,
    offset: nextOffset(),
  });
  const response = http.get(`${config.restBaseUrl}/api/readings?${query}`);

  check(response, {
    'REST selective monitoring accepted': (r) => r.status === 200,
    'REST returned readings array': (r) => Array.isArray(JSON.parse(r.body)),
  });
}

export function grpcSelectiveMonitoring() {
  loadGrpc();
  const response = grpcClient.invoke('iot.v1.ReadingsService/ListReadings', {
    deviceId: config.grpcDeviceId,
    sensorCodes: config.selectiveSensors,
    limit: 25,
    offset: nextOffset(),
  });

  check(response, {
    'gRPC selective monitoring accepted': (r) => r && r.status === StatusOK,
    'gRPC returned readings array': (r) => r && r.message && Array.isArray(r.message.readings),
  });
}

export function graphqlSelectiveMonitoring() {
  const response = http.post(
    config.graphqlUrl,
    graphqlListReadingsBody(25, nextOffset()),
    { headers: { 'Content-Type': 'application/json' } },
  );

  check(response, {
    'GraphQL selective monitoring accepted': (r) => r.status === 200,
    'GraphQL returned readings array': (r) => {
      const body = JSON.parse(r.body);
      return body.data && Array.isArray(body.data.listReadings);
    },
  });
}
