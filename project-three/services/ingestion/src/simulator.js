'use strict';

// Svi senzori iz air quality dataseta
const SENSOR_TYPES = [
  { code: 'co_gt',        unit: 'mg/m^3',         min: 0.1,  max: 11.9 },
  { code: 'pt08_s1_co',   unit: 'sensor response', min: 647,  max: 2040 },
  { code: 'nmhc_gt',      unit: 'microg/m^3',      min: 7,    max: 1189 },
  { code: 'c6h6_gt',      unit: 'microg/m^3',      min: 0.1,  max: 63.7 },
  { code: 'pt08_s2_nmhc', unit: 'sensor response', min: 383,  max: 2214 },
  { code: 'nox_gt',       unit: 'ppb',             min: 2,    max: 1479 },
  { code: 'pt08_s3_nox',  unit: 'sensor response', min: 322,  max: 2683 },
  { code: 'no2_gt',       unit: 'microg/m^3',      min: 2,    max: 340  },
  { code: 'pt08_s4_no2',  unit: 'sensor response', min: 551,  max: 2775 },
  { code: 'pt08_s5_o3',   unit: 'sensor response', min: 221,  max: 2523 },
  { code: 'temperature',  unit: 'celsius',         min: -1.9, max: 44.6 },
  { code: 'relative_humidity', unit: 'percent',    min: 9.2,  max: 88.7 },
  { code: 'absolute_humidity', unit: 'g/m^3',      min: 0.18, max: 2.23 },
];

function randomInRange(min, max) {
  return Math.round((min + Math.random() * (max - min)) * 100) / 100;
}

/**
 * Generiše jedan payload koji simulira očitavanje svih senzora sa uređaja.
 * @param {string} deviceId
 * @param {boolean} forceAlert - ako true, temperatura će biti > 50 (za Scenario D)
 */
function generateReading(deviceId, forceAlert = false) {
  const readings = {};
  for (const sensor of SENSOR_TYPES) {
    if (sensor.code === 'temperature' && forceAlert) {
      readings[sensor.code] = randomInRange(51, 60);
    } else {
      readings[sensor.code] = randomInRange(sensor.min, sensor.max);
    }
  }

  return {
    deviceId,
    timestamp: Date.now(),
    readings,
  };
}

/**
 * Pokreće N paralelnih uređaja koji publishuju na intervalima.
 * @param {number} deviceCount
 * @param {number} intervalMs - razmak između poruka po uređaju
 * @param {function} publishFn - async (payload) => void
 * @param {object} opts - { forceAlert, durationMs }
 * @returns {{ stop: function, stats: function }}
 */
function startSimulation(deviceCount, intervalMs, publishFn, opts = {}) {
  const { forceAlert = false, durationMs = 0 } = opts;

  let sent = 0;
  let errors = 0;
  const timers = [];

  for (let i = 0; i < deviceCount; i++) {
    const deviceId = `device-${String(i + 1).padStart(5, '0')}`;

    const timer = setInterval(async () => {
      try {
        const payload = generateReading(deviceId, forceAlert);
        await publishFn(JSON.stringify(payload));
        sent++;
      } catch (err) {
        errors++;
      }
    }, intervalMs);

    timers.push(timer);
  }

  // Automatski zaustavi nakon durationMs ako je zadato
  if (durationMs > 0) {
    setTimeout(() => stop(), durationMs);
  }

  function stop() {
    timers.forEach(t => clearInterval(t));
    timers.length = 0;
    console.log(`[simulator] Stopped. sent=${sent} errors=${errors}`);
  }

  function stats() {
    return { sent, errors, deviceCount, intervalMs };
  }

  return { stop, stats };
}

module.exports = { generateReading, startSimulation, SENSOR_TYPES };