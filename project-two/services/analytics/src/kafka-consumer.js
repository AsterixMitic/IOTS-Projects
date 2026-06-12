'use strict';

const { Kafka } = require('kafkajs');

let consumer = null;

/**
 * @param {string} brokerUrl
 * @param {string} topic
 * @param {function} onMessage - (payload: object) => void
 */
async function connect(brokerUrl, topic, onMessage) {
  const brokers = brokerUrl
    ? brokerUrl.replace(/^kafka:\/\//, '').split(',')
    : ['kafka:9092'];

  const kafka = new Kafka({
    clientId: 'analytics-service',
    brokers,
    retry: { retries: 5, initialRetryTime: 300 },
  });

  consumer = kafka.consumer({
    groupId: 'analytics-group',
    sessionTimeout: 30000,
    heartbeatInterval: 3000,
  });

  await consumer.connect();
  await consumer.subscribe({ topic, fromBeginning: false });

  console.log(`[kafka-consumer] Connected to ${brokers} | topic: ${topic}`);

  // run() je non-blocking — poruke stižu kroz eachMessage callback
  await consumer.run({
    eachMessage: async ({ message }) => {
      try {
        const payload = JSON.parse(message.value.toString());
        onMessage(payload);
      } catch (err) {
        console.warn('[kafka-consumer] Failed to parse message:', err.message);
      }
    },
  });
}

async function disconnect() {
  if (!consumer) return;
  await consumer.disconnect();
}

module.exports = { connect, disconnect };