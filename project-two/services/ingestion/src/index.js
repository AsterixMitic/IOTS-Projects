'use strict';

const http = require('http');
const { URL } = require('url');
const { startSimulation } = require('./simulator');
const mqttPublisher = require('./mqtt-publisher');
const kafkaPublisher = require('./kafka-publisher');

const serviceName = process.env.SERVICE_NAME || 'ingestion';
const brokerMode  = (process.env.BROKER_MODE || 'mqtt').toLowerCase();
const port        = Number(process.env.PORT || 3000);

const config = {
  serviceName,
  brokerMode,
  brokerUrl:  process.env.BROKER_URL  || '',
  mqttTopic:  process.env.MQTT_TOPIC  || 'iot/readings',
  kafkaTopic: process.env.KAFKA_TOPIC || 'iot.readings',
  mqttQos:    Number(process.env.MQTT_QOS   ?? 1),
  kafkaAcks:  Number(process.env.KAFKA_ACKS ?? 1),
};

// Aktivan publisher i simulacija
const publisher = brokerMode === 'kafka' ? kafkaPublisher : mqttPublisher;
const topic     = brokerMode === 'kafka' ? config.kafkaTopic : config.mqttTopic;

let simulation = null;

function sendJson(res, statusCode, payload) {
  const body = JSON.stringify(payload);
  res.writeHead(statusCode, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(body),
  });
  res.end(body);
}

function parseBody(req) {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', chunk => (data += chunk));
    req.on('end', () => {
      try { resolve(data ? JSON.parse(data) : {}); }
      catch { reject(new Error('Invalid JSON')); }
    });
  });
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);

  // GET /health
  if (req.method === 'GET' && url.pathname === '/health') {
    return sendJson(res, 200, {
      status: 'ok',
      service: serviceName,
      brokerMode,
      simulation: simulation ? simulation.stats() : null,
    });
  }

  // GET /config
  if (req.method === 'GET' && url.pathname === '/config') {
    return sendJson(res, 200, config);
  }

  // POST /simulate/start  { deviceCount, intervalMs, forceAlert, durationMs }
  if (req.method === 'POST' && url.pathname === '/simulate/start') {
    if (simulation) {
      return sendJson(res, 409, { error: 'Simulation already running. POST /simulate/stop first.' });
    }

    const body = await parseBody(req).catch(() => ({}));
    const deviceCount = Number(body.deviceCount ?? 1);
    const intervalMs  = Number(body.intervalMs  ?? 1000);
    const forceAlert  = Boolean(body.forceAlert  ?? false);
    const durationMs  = Number(body.durationMs   ?? 0);

    simulation = startSimulation(
      deviceCount,
      intervalMs,
      (msg) => publisher.publish(topic, msg),
      { forceAlert, durationMs },
    );

    if (durationMs > 0) {
      setTimeout(() => { simulation = null; }, durationMs + 100);
    }

    return sendJson(res, 202, {
      started: true,
      deviceCount,
      intervalMs,
      forceAlert,
      durationMs,
      brokerMode,
      topic,
    });
  }

  // POST /simulate/stop
  if (req.method === 'POST' && url.pathname === '/simulate/stop') {
    if (!simulation) {
      return sendJson(res, 409, { error: 'No simulation running.' });
    }
    const stats = simulation.stats();
    simulation.stop();
    simulation = null;
    return sendJson(res, 200, { stopped: true, stats });
  }

  // GET /simulate/stats
  if (req.method === 'GET' && url.pathname === '/simulate/stats') {
    return sendJson(res, 200, simulation ? simulation.stats() : { running: false });
  }

  return sendJson(res, 404, { error: 'Not found' });
});

// Povezi se na broker pa pokreni server
publisher.connect(config.brokerUrl, topic)
  .then(() => {
    server.listen(port, '0.0.0.0', () => {
      console.log(`[${serviceName}] Listening on ${port} | mode=${brokerMode} | topic=${topic}`);
    });
  })
  .catch((err) => {
    console.error(`[${serviceName}] Failed to connect to broker:`, err.message);
    process.exit(1);
  });

// Graceful shutdown
process.on('SIGTERM', async () => {
  console.log(`[${serviceName}] SIGTERM received, shutting down...`);
  if (simulation) simulation.stop();
  await publisher.disconnect();
  server.close(() => process.exit(0));
});