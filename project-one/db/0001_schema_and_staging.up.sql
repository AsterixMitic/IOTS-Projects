-- Migration 0001: create core schema and staging table for CSV import

BEGIN;

CREATE TABLE IF NOT EXISTS devices (
    id BIGSERIAL PRIMARY KEY,
    external_id TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    location TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS sensor_types (
    id BIGSERIAL PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    label TEXT NOT NULL,
    unit TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS readings (
    id BIGSERIAL PRIMARY KEY,
    device_id BIGINT NOT NULL REFERENCES devices(id),
    recorded_at TIMESTAMPTZ NOT NULL,
    source_date DATE,
    source_time TIME,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS reading_values (
    reading_id BIGINT NOT NULL REFERENCES readings(id) ON DELETE CASCADE,
    sensor_type_id BIGINT NOT NULL REFERENCES sensor_types(id),
    numeric_value NUMERIC,
    text_value TEXT,
    PRIMARY KEY (reading_id, sensor_type_id)
);

CREATE INDEX IF NOT EXISTS idx_readings_device_recorded_at
    ON readings (device_id, recorded_at DESC);

CREATE INDEX IF NOT EXISTS idx_readings_recorded_at
    ON readings (recorded_at DESC);

CREATE INDEX IF NOT EXISTS idx_reading_values_sensor_type
    ON reading_values (sensor_type_id);

-- Staging table: simple text columns to match CSV order
CREATE TABLE IF NOT EXISTS staging_airquality (
    date_col TEXT,
    time_col TEXT,
    co_gt TEXT,
    pt08_s1_co TEXT,
    nmhc_gt TEXT,
    c6h6_gt TEXT,
    pt08_s2_nmhc TEXT,
    nox_gt TEXT,
    pt08_s3_nox TEXT,
    no2_gt TEXT,
    pt08_s4_no2 TEXT,
    pt08_s5_o3 TEXT,
    temperature TEXT,
    rh TEXT,
    ah TEXT,
    -- extra columns to tolerate trailing/ancillary CSV fields
    extra1 TEXT,
    extra2 TEXT
);

-- Seed device and sensor types (idempotent)
INSERT INTO devices (external_id, name, location)
VALUES ('air-quality-station-01', 'Air Quality Station 01', 'Italian city roadside')
ON CONFLICT (external_id) DO NOTHING;

INSERT INTO sensor_types (code, label, unit)
VALUES
    ('co_gt', 'CO (GT)', 'mg/m^3'),
    ('pt08_s1_co', 'PT08.S1 (CO)', 'sensor response'),
    ('nmhc_gt', 'NMHC (GT)', 'microg/m^3'),
    ('c6h6_gt', 'C6H6 (GT)', 'microg/m^3'),
    ('pt08_s2_nmhc', 'PT08.S2 (NMHC)', 'sensor response'),
    ('nox_gt', 'NOx (GT)', 'ppb'),
    ('pt08_s3_nox', 'PT08.S3 (NOx)', 'sensor response'),
    ('no2_gt', 'NO2 (GT)', 'microg/m^3'),
    ('pt08_s4_no2', 'PT08.S4 (NO2)', 'sensor response'),
    ('pt08_s5_o3', 'PT08.S5 (O3)', 'sensor response'),
    ('temperature', 'Temperature', 'celsius'),
    ('relative_humidity', 'Relative Humidity', 'percent'),
    ('absolute_humidity', 'Absolute Humidity', 'g/m^3')
ON CONFLICT (code) DO NOTHING;

COMMIT;
