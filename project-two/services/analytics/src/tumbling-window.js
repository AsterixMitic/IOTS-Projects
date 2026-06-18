'use strict';

/**
 * Tumbling Window — fiksni vremenski prozor koji se ne preklapa.
 * Svakih windowSeconds sekundi računa prosek temperature.
 * Ako je prosek > alertThreshold, ispisuje ALERT u log.
 */
class TumblingWindow {
  constructor(windowSeconds, alertThreshold) {
    this.windowSeconds   = windowSeconds;
    this.alertThreshold  = alertThreshold;
    this.temperatures    = [];   // vrednosti u tekućem prozoru
    this.windowCount     = 0;    // koliko je prozora prošlo
    this.alertCount      = 0;    // koliko je alerta okidano
    this.timer           = null;
  }

  start() {
    if (this.timer) return;

    this.timer = setInterval(() => {
      this._flush();
    }, this.windowSeconds * 1000);

    console.log(
      `[tumbling-window] Started — windowSeconds=${this.windowSeconds} alertThreshold=${this.alertThreshold}°C`
    );
  }

  stop() {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  /**
   * Dodaj novu temperaturu u tekući prozor.
   * @param {number} temperature
   * @param {string} deviceId
   * @param {number} msgTimestamp - timestamp iz payloada (za latenciju Scenario D)
   */
  addReading(temperature, deviceId, msgTimestamp) {
    const receiveTime = Date.now();
    const latencyMs   = msgTimestamp ? receiveTime - msgTimestamp : null;

    this.temperatures.push({ value: temperature, deviceId, latencyMs });
  }

  /**
   * Zatvori prozor, izračunaj statistiku, okini alert ako treba.
   */
  _flush() {
    this.windowCount++;
    const count = this.temperatures.length;

    if (count === 0) {
      console.log(`[window #${this.windowCount}] No readings in window.`);
      this.temperatures = [];
      return;
    }

    const values  = this.temperatures.map(r => r.value);
    const avg     = values.reduce((a, b) => a + b, 0) / count;
    const min     = Math.min(...values);
    const max     = Math.max(...values);
    const latencies = this.temperatures
      .map(r => r.latencyMs)
      .filter(l => l !== null);
    const avgLatency = latencies.length
      ? Math.round(latencies.reduce((a, b) => a + b, 0) / latencies.length)
      : null;

    const avgRounded = Math.round(avg * 100) / 100;
    const isAlert    = avgRounded > this.alertThreshold;

    if (isAlert) {
      this.alertCount++;
      console.error(
        `[ALERT] Window #${this.windowCount} | avg_temp=${avgRounded}°C > threshold=${this.alertThreshold}°C | ` +
        `count=${count} min=${min} max=${max} | avg_latency=${avgLatency}ms`
      );
    } else {
      console.log(
        `[window #${this.windowCount}] avg_temp=${avgRounded}°C | ` +
        `count=${count} min=${min} max=${max} | avg_latency=${avgLatency}ms`
      );
    }

    // Reset za sledeći prozor
    this.temperatures = [];
  }

  stats() {
    return {
      windowCount:  this.windowCount,
      alertCount:   this.alertCount,
      windowSeconds: this.windowSeconds,
      alertThreshold: this.alertThreshold,
      currentBufferSize: this.temperatures.length,
    };
  }
}

module.exports = { TumblingWindow };