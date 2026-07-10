'use strict';

const mqtt = require('mqtt');

const QOS_LEVEL = parseInt(process.env.MQTT_QOS ?? '1', 10);

let client = null;
let connected = false;

function connect(brokerUrl, topic) {
  return new Promise((resolve, reject) => {
    const url = brokerUrl || 'mqtt://mosquitto:1883';

    client = mqtt.connect(url, {
      clientId: `ingestion-${process.pid}`,
      clean: true,
      connectTimeout: 5000,
      reconnectPeriod: 1000,
    });

    client.on('connect', () => {
      connected = true;
      console.log(`[mqtt-publisher] Connected to ${url} | QoS ${QOS_LEVEL} | topic: ${topic}`);
      resolve();
    });

    client.on('error', (err) => {
      if (!connected) reject(err);
      else console.error('[mqtt-publisher] Error:', err.message);
    });

    client.on('offline', () => {
      connected = false;
      console.warn('[mqtt-publisher] Offline');
    });

    client.on('reconnect', () => {
      console.log('[mqtt-publisher] Reconnecting...');
    });
  });
}

function publish(topic, message) {
  return new Promise((resolve, reject) => {
    if (!client || !connected) {
      return reject(new Error('MQTT client not connected'));
    }

    client.publish(topic, message, { qos: QOS_LEVEL }, (err) => {
      if (err) reject(err);
      else resolve();
    });
  });
}

function disconnect() {
  return new Promise((resolve) => {
    if (!client) return resolve();
    client.end(false, {}, resolve);
  });
}

module.exports = { connect, publish, disconnect };