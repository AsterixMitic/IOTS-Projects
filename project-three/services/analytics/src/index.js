'use strict';

// Analytics (Projekat 3, Faza 4) — orkestrira tri izvora analize:
//  1) iot/stored  -> tumbling window (agregacija/alarm) + prosečno očitavanje za MaaS
//  2) iot/events  -> CEP događaji iz eKuiper-a
//  3) MaaS REST   -> klasifikacija kvaliteta vazduha po prozoru
// Objedinjeni rezultat publikuje na iot/analytics i izlaže preko REST-a.

const http = require('http');
const { URL } = require('url');
const { TumblingWindow } = require('./tumbling-window');
const bus  = require('./mqtt-bus');
const maas = require('./maas-client');

const serviceName    = process.env.SERVICE_NAME     || 'analytics';
const port           = Number(process.env.PORT            || 3001);
const windowSeconds  = Number(process.env.WINDOW_SECONDS  || 10);
const alertThreshold = Number(process.env.ALERT_THRESHOLD || 50);
const qos            = parseInt(process.env.MQTT_QOS ?? '1', 10);

const config = {
  serviceName,
  brokerMode:     'mqtt',
  brokerUrl:      process.env.BROKER_URL || '',
  storedTopic:    process.env.MQTT_STORED_TOPIC    || process.env.MQTT_TOPIC || 'iot/stored',
  eventsTopic:    process.env.MQTT_EVENTS_TOPIC    || 'iot/events',
  analyticsTopic: process.env.MQTT_ANALYTICS_TOPIC || 'iot/analytics',
  maasUrl:        process.env.MAAS_URL || 'http://maas:8000',
  maasEnabled:   (process.env.MAAS_ENABLED || 'true') === 'true',
  windowSeconds,
  alertThreshold,
};

// ── In-memory ring buferi (poslednjih MAX) ──────────────────────────────────
const MAX = 50;
const recentEvents      = [];
const recentPredictions = [];
const recentAlerts      = [];
const eventCounts       = {};   // type -> count

function pushCapped(arr, item) {
  arr.unshift(item);
  if (arr.length > MAX) arr.pop();
}

function pushAlert(alert) {
  pushCapped(recentAlerts, { ...alert, at: Date.now() });
}

// ── Tumbling window sa MaaS + summary po zatvaranju prozora ──────────────────
const window = new TumblingWindow(windowSeconds, alertThreshold, onWindowFlush);

async function onWindowFlush(result) {
  let prediction = null;

  if (config.maasEnabled && result.avgFeatures) {
    prediction = await maas.predict(result.avgFeatures);
    if (prediction) {
      pushCapped(recentPredictions, { ...prediction, window: result.windowNumber, at: Date.now() });
    }
  }

  // Agregacija alarma: temperaturni prag + ML klasa 'unhealthy'.
  if (result.isAlert) {
    pushAlert({ source: 'window', reason: `avg_temp ${result.avgTemp}°C > ${alertThreshold}°C`, window: result.windowNumber });
  }
  if (prediction && prediction.air_quality === 'unhealthy') {
    pushAlert({ source: 'maas', reason: 'ML klasa: unhealthy', probabilities: prediction.probabilities, window: result.windowNumber });
  }

  // Objedinjeni izlaz na iot/analytics (koristi ga i web dashboard).
  const summary = {
    type:          'ANALYTICS_SUMMARY',
    window:        result.windowNumber,
    count:         result.count,
    avgTemp:       result.avgTemp,
    min:           result.min,
    max:           result.max,
    avgLatencyMs:  result.avgLatency,
    tempAlert:     result.isAlert,
    airQuality:    prediction ? prediction.air_quality : null,
    airQualityProb: prediction ? prediction.probabilities : null,
    ts:            Date.now(),
  };
  await bus.publish(config.analyticsTopic, summary, qos);
}

// ── Ruter poruka po topicu ──────────────────────────────────────────────────
function onMessage(topic, payload) {
  if (topic === config.eventsTopic) {
    const evt = { ...payload, receivedAt: Date.now() };
    pushCapped(recentEvents, evt);
    const type = payload.type || 'UNKNOWN';
    eventCounts[type] = (eventCounts[type] || 0) + 1;

    // Kritični CEP događaji idu i u alarme.
    if (payload.severity === 'critical' || type === 'POLLUTION_SPIKE') {
      pushAlert({ source: 'ekuiper', reason: type, deviceId: payload.deviceId, event: payload });
    }
    return;
  }

  // Podrazumevano: perzistirano očitavanje sa iot/stored.
  const readings = payload && payload.readings;
  if (readings && typeof readings === 'object') {
    window.addReading(readings, payload.deviceId, payload.timestamp);
  }
}

// ── HTTP API ────────────────────────────────────────────────────────────────
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

  if (req.method === 'GET' && url.pathname === '/health') {
    return sendJson(res, 200, {
      status: 'ok',
      service: serviceName,
      brokerMode: config.brokerMode,
      window: window.stats(),
      events: { total: recentEvents.length, byType: eventCounts },
      predictions: recentPredictions.length,
      maas: maas.status(),
    });
  }

  if (req.method === 'GET' && url.pathname === '/config') {
    return sendJson(res, 200, config);
  }

  if (req.method === 'GET' && url.pathname === '/window/stats') {
    return sendJson(res, 200, window.stats());
  }

  // Poslednji CEP događaji iz eKuiper-a.
  if (req.method === 'GET' && url.pathname === '/events') {
    return sendJson(res, 200, { total: recentEvents.length, byType: eventCounts, recent: recentEvents });
  }

  // Poslednje MaaS predikcije.
  if (req.method === 'GET' && url.pathname === '/predictions') {
    return sendJson(res, 200, { count: recentPredictions.length, maas: maas.status(), recent: recentPredictions });
  }

  // Objedinjeni alarmi (prag + CEP + ML).
  if (req.method === 'GET' && url.pathname === '/alerts') {
    return sendJson(res, 200, { count: recentAlerts.length, recent: recentAlerts });
  }

  return sendJson(res, 404, { error: 'Not found' });
});

// ── Povezivanje ─────────────────────────────────────────────────────────────
bus.onMessage(onMessage);

bus.connect(config.brokerUrl, `analytics-${process.pid}`)
  .then(() => bus.subscribe([config.storedTopic, config.eventsTopic], qos))
  .then(() => {
    window.start();
    server.listen(port, '0.0.0.0', () => {
      console.log(`[${serviceName}] Listening on ${port} | stored=${config.storedTopic} events=${config.eventsTopic} | maas=${config.maasEnabled ? config.maasUrl : 'disabled'}`);
    });
  })
  .catch((err) => {
    console.error(`[${serviceName}] Failed to start:`, err.message);
    process.exit(1);
  });

// ── Graceful shutdown ────────────────────────────────────────────────────────
process.on('SIGTERM', async () => {
  console.log(`[${serviceName}] SIGTERM received, shutting down...`);
  window.stop();
  await bus.disconnect();
  server.close(() => process.exit(0));
});
