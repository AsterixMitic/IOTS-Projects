'use strict';

const mqtt = require('mqtt');

const QOS_LEVEL = parseInt(process.env.MQTT_QOS ?? '1', 10);

let client = null;

/**
 * @param {string} brokerUrl
 * @param {string} topic
 * @param {function} onMessage - (payload: object) => void
 */
function connect(brokerUrl, topic, onMessage) {
  return new Promise((resolve, reject) => {
    const url = brokerUrl || 'mqtt://mosquitto:1883';

    client = mqtt.connect(url, {
      clientId: `analytics-${process.pid}`,
      clean:    false,   // persistent session — bitan za Scenario B (recovery)
      connectTimeout: 5000,
      reconnectPeriod: 1000,
    });

    client.on('connect', () => {
      console.log(`[mqtt-consumer] Connected to ${url} | QoS ${QOS_LEVEL} | topic: ${topic}`);

      client.subscribe(topic, { qos: QOS_LEVEL }, (err) => {
        if (err) return reject(err);
        resolve();
      });
    });

    client.on('message', (receivedTopic, buffer) => {
      try {
        const payload = JSON.parse(buffer.toString());
        onMessage(payload);
      } catch (err) {
        console.warn('[mqtt-consumer] Failed to parse message:', err.message);
      }
    });

    client.on('error',   (err) => console.error('[mqtt-consumer] Error:', err.message));
    client.on('offline', ()    => console.warn('[mqtt-consumer] Offline — waiting for reconnect'));
    client.on('reconnect', ()  => console.log('[mqtt-consumer] Reconnecting...'));
  });
}

function disconnect() {
  return new Promise((resolve) => {
    if (!client) return resolve();
    client.end(false, {}, resolve);
  });
}

module.exports = { connect, disconnect };