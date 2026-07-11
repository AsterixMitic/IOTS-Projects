'use strict';

// MQTT bus za Analytics: jedan klijent, pretplata na više topica + publikovanje.
const mqtt = require('mqtt');

let client = null;
let handler = () => {};

/**
 * @param {string} brokerUrl
 * @param {string} clientId
 */
function connect(brokerUrl, clientId) {
  return new Promise((resolve, reject) => {
    const url = brokerUrl || 'mqtt://mosquitto:1883';
    client = mqtt.connect(url, {
      clientId: clientId || `analytics-${process.pid}`,
      clean: false,            // persistent session (recovery)
      connectTimeout: 5000,
      reconnectPeriod: 1000,
    });

    let settled = false;

    client.on('connect', () => {
      console.log(`[mqtt-bus] Connected to ${url}`);
      if (!settled) { settled = true; resolve(); }
    });

    client.on('message', (topic, buffer) => {
      let payload;
      try {
        payload = JSON.parse(buffer.toString());
      } catch (err) {
        return console.warn(`[mqtt-bus] Bad JSON on ${topic}: ${err.message}`);
      }
      try {
        handler(topic, payload);
      } catch (err) {
        console.error(`[mqtt-bus] handler error on ${topic}: ${err.message}`);
      }
    });

    client.on('error',   (err) => {
      console.error('[mqtt-bus] Error:', err.message);
      if (!settled) { settled = true; reject(err); }
    });
    client.on('offline',   () => console.warn('[mqtt-bus] Offline — waiting for reconnect'));
    client.on('reconnect', () => console.log('[mqtt-bus] Reconnecting...'));
  });
}

/** Registruj jedinstveni handler: (topic, payload) => void */
function onMessage(fn) { handler = fn; }

/** Pretplati se na jedan ili više topica. */
function subscribe(topics, qos = 1) {
  const list = Array.isArray(topics) ? topics : [topics];
  return new Promise((resolve, reject) => {
    client.subscribe(list, { qos }, (err) => {
      if (err) return reject(err);
      console.log(`[mqtt-bus] Subscribed: ${list.join(', ')} (QoS ${qos})`);
      resolve();
    });
  });
}

/** Publikuj objekat kao JSON (best-effort — ne baca ako klijent nije spreman). */
function publish(topic, obj, qos = 1) {
  return new Promise((resolve) => {
    if (!client || !client.connected) return resolve(false);
    client.publish(topic, JSON.stringify(obj), { qos }, (err) => {
      if (err) console.warn('[mqtt-bus] publish failed:', err.message);
      resolve(!err);
    });
  });
}

function disconnect() {
  return new Promise((resolve) => {
    if (!client) return resolve();
    client.end(false, {}, resolve);
  });
}

module.exports = { connect, onMessage, subscribe, publish, disconnect };
