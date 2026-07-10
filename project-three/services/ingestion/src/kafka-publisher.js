'use strict';

const { Kafka, CompressionTypes } = require('kafkajs');

// acks: 0 = fire and forget, 1 = leader only, -1 = all replicas
const ACKS = parseInt(process.env.KAFKA_ACKS ?? '1', 10);

let producer = null;

function connect(brokerUrl, topic) {
  const brokers = brokerUrl
    ? brokerUrl.replace(/^kafka:\/\//, '').split(',')
    : ['kafka:9092'];

  const kafka = new Kafka({
    clientId: 'ingestion-service',
    brokers,
    retry: { retries: 5, initialRetryTime: 300 },
  });

  producer = kafka.producer({
    allowAutoTopicCreation: true,
    transactionTimeout: 30000,
  });

  return producer.connect().then(() => {
    console.log(`[kafka-publisher] Connected to ${brokers} | acks=${ACKS} | topic: ${topic}`);
  });
}

function publish(topic, message) {
  if (!producer) {
    return Promise.reject(new Error('Kafka producer not connected'));
  }

  return producer.send({
    topic,
    acks: ACKS,
    messages: [{ value: message }],
  });
}

function disconnect() {
  if (!producer) return Promise.resolve();
  return producer.disconnect();
}

module.exports = { connect, publish, disconnect };