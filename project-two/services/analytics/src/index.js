'use strict';

const http = require('http');
const { URL } = require('url');
const { TumblingWindow } = require('./tumbling-window');
const mqttConsumer  = require('./mqtt-consumer');
const kafkaConsumer = require('./kafka-consumer');

const serviceName    = process.env.SERVICE_NAME    || 'analytics';
const brokerMode     = (process.env.BROKER_MODE    || 'mqtt').toLowerCase();
const port           = Number(process.env.PORT           || 3001);
const windowSeconds  = Number(process.env.WINDOW_SECONDS  || 10);
const alertThreshold = Number(process.env.ALERT_THRESHOLD || 50);

const config = {
  serviceName,
  brokerMode,
  brokerUrl:      process.env.BROKER_URL   || '',
  mqttTopic:      process.env.MQTT_TOPIC   || 'iot/readings',
  kafkaTopic:     process.env.KAFKA_TOPIC  || 'iot.readings',
  windowSeconds,
  alertThreshold,
};

const consumer = brokerMode === 'kafka' ? kafkaConsumer : mqttConsumer;
const topic    = brokerMode === 'kafka' ? config.kafkaTopic : config.mqttTopic;
const window   = new TumblingWindow(windowSeconds, alertThreshold);

// Callback koji se poziva za svaku pristiglu poruku
function onMessage(payload) {
  const temp = payload?.readings?.temperature;
  if (typeof temp !== 'number') return;

  window.addReading(temp, payload.deviceId, payload.timestamp);
}

function sendJson(res, statusCode, payload) {
  const body = JSON.stringify(payload);
  res.writeHead(statusCode, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(body),
  });
  res.end(body);
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);

  // GET /health
  if (req.method === 'GET' && url.pathname === '/health') {
    return sendJson(res, 200, {
      status: 'ok',
      service: serviceName,
      brokerMode,
      window: window.stats(),
    });
  }

  // GET /config
  if (req.method === 'GET' && url.pathname === '/config') {
    return sendJson(res, 200, config);
  }

  // GET /window/stats
  if (req.method === 'GET' && url.pathname === '/window/stats') {
    return sendJson(res, 200, window.stats());
  }

  return sendJson(res, 404, { error: 'Not found' });
});

// Povezi se na broker, pokreni window, pa server
consumer.connect(config.brokerUrl, topic, onMessage)
  .then(() => {
    window.start();
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
  window.stop();
  await consumer.disconnect();
  server.close(() => process.exit(0));
});