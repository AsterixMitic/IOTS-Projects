'use strict';

// Klijent za MaaS REST servis (klasifikacija kvaliteta vazduha).
// Robustan: timeout + graceful degradacija (vraća null umesto da baca).
const MAAS_URL   = process.env.MAAS_URL || 'http://maas:8000';
const TIMEOUT_MS = Number(process.env.MAAS_TIMEOUT_MS || 3000);

let lastStatus = { reachable: false, lastError: null, lastOkAt: null, calls: 0, failures: 0 };

/**
 * @param {object} readings - objekat sa senzorskim vrednostima (MaaS bira svoje features)
 * @returns {Promise<object|null>} { air_quality, probabilities, model_version } ili null
 */
async function predict(readings) {
  lastStatus.calls++;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const res = await fetch(`${MAAS_URL}/predict`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ readings }),
      signal: controller.signal,
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    lastStatus = { reachable: true, lastError: null, lastOkAt: Date.now(), calls: lastStatus.calls, failures: lastStatus.failures };
    return data;
  } catch (err) {
    lastStatus = { reachable: false, lastError: err.message, lastOkAt: lastStatus.lastOkAt, calls: lastStatus.calls, failures: lastStatus.failures + 1 };
    console.warn(`[maas-client] predict failed: ${err.message}`);
    return null;
  } finally {
    clearTimeout(timer);
  }
}

function status() {
  return { url: MAAS_URL, ...lastStatus };
}

module.exports = { predict, status };
