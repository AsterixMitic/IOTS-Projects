export const config = {
  restBaseUrl: __ENV.REST_BASE_URL || 'http://host.docker.internal:5000',
  graphqlUrl: __ENV.GRAPHQL_URL || 'http://host.docker.internal:8000/graphql',
  grpcAddr: __ENV.GRPC_ADDR || 'host.docker.internal:50051',
  grpcProtoDir: __ENV.GRPC_PROTO_DIR || '../../grpc-service-go/proto',
  restDeviceId: Number(__ENV.REST_DEVICE_ID || 1),
  grpcDeviceId: Number(__ENV.GRPC_DEVICE_ID || 1),
  graphqlDeviceId: __ENV.GRAPHQL_DEVICE_ID || 'air-quality-station-01',
  selectiveSensors: (__ENV.SELECTIVE_SENSORS || 'temperature,relative_humidity,absolute_humidity')
    .split(',')
    .map((value) => value.trim().toLowerCase())
    .filter(Boolean),
  historicalSensorCode: (__ENV.HISTORICAL_SENSOR_CODE || 'temperature').trim().toLowerCase(),
  // The UCI Air Quality dataset covers 2004-03-10 .. 2005-04-04, so historical
  // queries must target that window (not "now") or they come back empty.
  historicalFrom: __ENV.LOADTEST_HISTORICAL_FROM || '2004-03-10T00:00:00Z',
  historicalTo: __ENV.LOADTEST_HISTORICAL_TO || '2005-04-05T00:00:00Z',
};

// --- Load-profile helpers ------------------------------------------------
// The assignment requires simulating 10 / 100 / 500 virtual users. We use the
// constant-vus executor so `VUS` maps 1:1 to concurrent virtual users; RPS then
// emerges as an output metric (http_reqs / grpc_reqs rate).
export const load = {
  vus: Number(__ENV.VUS || 10),
  duration: __ENV.DURATION || '30s',
  // 'all' runs every protocol concurrently; set PROTOCOL=rest|grpc|graphql to
  // isolate one protocol for a clean, contention-free comparison.
  protocol: (__ENV.PROTOCOL || 'all').toLowerCase(),
};

export function protocolEnabled(name) {
  return load.protocol === 'all' || load.protocol === name;
}

export function vusScenario(exec) {
  return {
    executor: 'constant-vus',
    exec,
    vus: load.vus,
    duration: load.duration,
  };
}

// execByProtocol: { rest: 'restFn', grpc: 'grpcFn', graphql: 'graphqlFn' }
export function buildScenarios(execByProtocol) {
  const scenarios = {};
  for (const [protocol, exec] of Object.entries(execByProtocol)) {
    if (protocolEnabled(protocol)) {
      scenarios[`${protocol}_${exec}`] = vusScenario(exec);
    }
  }
  return scenarios;
}

export const sensorCodes = [
  'co_gt',
  'pt08_s1_co',
  'nmhc_gt',
  'c6h6_gt',
  'pt08_s2_nmhc',
  'nox_gt',
  'pt08_s3_nox',
  'no2_gt',
  'pt08_s4_no2',
  'pt08_s5_o3',
  'temperature',
  'relative_humidity',
  'absolute_humidity',
];

const sensorRanges = {
  co_gt: [0.1, 5.0],
  pt08_s1_co: [100, 2000],
  nmhc_gt: [0.1, 5.0],
  c6h6_gt: [0.1, 30.0],
  pt08_s2_nmhc: [100, 2000],
  nox_gt: [0.1, 200.0],
  pt08_s3_nox: [100, 2000],
  no2_gt: [0.1, 300.0],
  pt08_s4_no2: [100, 2000],
  pt08_s5_o3: [100, 2000],
  temperature: [10.0, 35.0],
  relative_humidity: [20.0, 90.0],
  absolute_humidity: [2.0, 20.0],
};

export function randomSensorCode() {
  return sensorCodes[Math.floor(Math.random() * sensorCodes.length)];
}

export function sensorValue(sensorCode) {
  const [min, max] = sensorRanges[sensorCode] || [0.0, 100.0];
  const value = min + Math.random() * (max - min);
  return Number(value.toFixed(2));
}

export function utcNow() {
  return new Date().toISOString();
}

export function utcBefore(hours) {
  return new Date(Date.now() - hours * 60 * 60 * 1000).toISOString();
}

export function buildQuery(params) {
  return Object.entries(params)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
    .join('&');
}

export function restCreateBody(sensorCode, measuredValue) {
  return JSON.stringify({
    deviceId: config.restDeviceId,
    recordedAt: utcNow(),
    values: {
      [sensorCode]: measuredValue,
    },
    notes: 'k6 load test',
  });
}

export function grpcCreateRequest(sensorCode, measuredValue) {
  return {
    deviceId: config.grpcDeviceId,
    recordedAt: utcNow(),
    notes: 'k6 load test',
    values: {
      [sensorCode]: measuredValue,
    },
  };
}

export function graphqlCreateBody(sensorCode, measuredValue) {
  return JSON.stringify({
    query: `mutation {
      createReading(
        deviceId: ${JSON.stringify(config.graphqlDeviceId)}
        sensorCode: ${JSON.stringify(sensorCode)}
        measuredValue: ${measuredValue}
        measuredAt: ${JSON.stringify(utcNow())}
      ) {
        id
      }
    }`,
  });
}

export function graphqlListReadingsBody(limit, offset) {
  return JSON.stringify({
    query: `query {
      listReadings(limit: ${limit}, offset: ${offset}) {
        id
        deviceId
        sensorCode
        measuredAt
      }
    }`,
  });
}

export function graphqlAggregateBody(sensorCode, from, to) {
  return JSON.stringify({
    query: `query {
      aggregateReadings(
        sensorCode: ${JSON.stringify(sensorCode)}
        startDate: ${JSON.stringify(from)}
        endDate: ${JSON.stringify(to)}
      ) {
        sensorCode
        measuredAt
        avgValue
        minValue
        maxValue
        count
      }
    }`,
  });
}
