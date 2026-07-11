'use strict';

/**
 * Tumbling Window — fiksni vremenski prozor koji se ne preklapa.
 * Svakih windowSeconds sekundi:
 *  - računa prosek/min/max temperature i latenciju,
 *  - računa PROSEČNO očitavanje (po svim numeričkim senzorima) za MaaS klasifikaciju,
 *  - okida ALERT ako je prosek temperature > alertThreshold,
 *  - poziva onFlush(result) sa objedinjenim rezultatom prozora.
 */
class TumblingWindow {
  constructor(windowSeconds, alertThreshold, onFlush) {
    this.windowSeconds  = windowSeconds;
    this.alertThreshold = alertThreshold;
    this.onFlush        = onFlush || (() => {});
    this.windowCount    = 0;
    this.alertCount     = 0;
    this.timer          = null;
    this._reset();
  }

  _reset() {
    this.temperatures = [];   // { value, deviceId, latencyMs }
    this.featureSums  = {};   // senzor -> suma vrednosti
    this.featureCount = 0;    // broj očitavanja u prozoru
  }

  start() {
    if (this.timer) return;
    this.timer = setInterval(() => this._flush(), this.windowSeconds * 1000);
    console.log(`[tumbling-window] Started — windowSeconds=${this.windowSeconds} alertThreshold=${this.alertThreshold}°C`);
  }

  stop() {
    if (this.timer) { clearInterval(this.timer); this.timer = null; }
  }

  /**
   * @param {object} readings     - ceo readings objekat (svi senzori)
   * @param {string} deviceId
   * @param {number} msgTimestamp - originalni timestamp (za latenciju)
   */
  addReading(readings, deviceId, msgTimestamp) {
    const temp = readings ? readings.temperature : undefined;
    if (typeof temp === 'number') {
      const latencyMs = msgTimestamp ? Date.now() - msgTimestamp : null;
      this.temperatures.push({ value: temp, deviceId, latencyMs });
    }
    for (const [key, val] of Object.entries(readings || {})) {
      if (typeof val === 'number') {
        this.featureSums[key] = (this.featureSums[key] || 0) + val;
      }
    }
    this.featureCount++;
  }

  _avgFeatures() {
    if (this.featureCount === 0) return null;
    const avg = {};
    for (const [key, sum] of Object.entries(this.featureSums)) {
      avg[key] = Math.round((sum / this.featureCount) * 1000) / 1000;
    }
    return avg;
  }

  _flush() {
    this.windowCount++;
    const count = this.temperatures.length;
    const avgFeatures = this._avgFeatures();

    let result;
    if (count === 0) {
      console.log(`[window #${this.windowCount}] No temperature readings.`);
      result = {
        windowNumber: this.windowCount, count: 0,
        avgTemp: null, min: null, max: null, avgLatency: null,
        isAlert: false, avgFeatures,
      };
    } else {
      const values    = this.temperatures.map(r => r.value);
      const avg       = values.reduce((a, b) => a + b, 0) / count;
      const min       = Math.min(...values);
      const max       = Math.max(...values);
      const latencies = this.temperatures.map(r => r.latencyMs).filter(l => l !== null);
      const avgLatency = latencies.length
        ? Math.round(latencies.reduce((a, b) => a + b, 0) / latencies.length)
        : null;
      const avgTemp   = Math.round(avg * 100) / 100;
      const isAlert   = avgTemp > this.alertThreshold;

      if (isAlert) {
        this.alertCount++;
        console.error(`[ALERT] Window #${this.windowCount} | avg_temp=${avgTemp}°C > threshold=${this.alertThreshold}°C | count=${count} min=${min} max=${max} | avg_latency=${avgLatency}ms`);
      } else {
        console.log(`[window #${this.windowCount}] avg_temp=${avgTemp}°C | count=${count} min=${min} max=${max} | avg_latency=${avgLatency}ms`);
      }

      result = { windowNumber: this.windowCount, count, avgTemp, min, max, avgLatency, isAlert, avgFeatures };
    }

    this._reset();

    // Ne blokiraj timer — onFlush može biti async (poziv MaaS-a, publish).
    Promise.resolve()
      .then(() => this.onFlush(result))
      .catch(err => console.error('[tumbling-window] onFlush error:', err.message));
  }

  stats() {
    return {
      windowCount:       this.windowCount,
      alertCount:        this.alertCount,
      windowSeconds:     this.windowSeconds,
      alertThreshold:    this.alertThreshold,
      currentBufferSize: this.temperatures.length,
    };
  }
}

module.exports = { TumblingWindow };
