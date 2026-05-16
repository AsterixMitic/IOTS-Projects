-- Migration 0002: transform staging rows into normalized readings + reading_values

BEGIN;

-- Truncate previous import data (idempotent import)
TRUNCATE reading_values, readings RESTART IDENTITY;

-- Insert readings (one row per timestamp)
WITH device AS (SELECT id AS device_id FROM devices WHERE external_id = 'air-quality-station-01')
INSERT INTO readings (device_id, recorded_at, source_date, source_time, notes)
SELECT device.device_id,
       (to_timestamp(s.date_col || ' ' || replace(s.time_col, '.', ':'), 'DD/MM/YYYY HH24:MI:SS'))::timestamptz,
       to_date(s.date_col, 'DD/MM/YYYY'),
       replace(s.time_col, '.', ':')::time,
       'Imported from staging'
FROM staging_airquality s, device;

-- Unpivot sensor columns and insert all values in a single set-based statement.
-- This replaces multiple repetitive INSERTs with a CROSS JOIN LATERAL VALUES approach.
INSERT INTO reading_values (reading_id, sensor_type_id, numeric_value)
SELECT r.id, st.id,
       NULLIF(replace(v.val, ',', '.'), '')::numeric
FROM readings r
JOIN staging_airquality s
  ON r.recorded_at = (to_timestamp(s.date_col || ' ' || replace(s.time_col, '.', ':'), 'DD/MM/YYYY HH24:MI:SS'))::timestamptz
CROSS JOIN LATERAL (
  VALUES
    (s.co_gt, 'co_gt'),
    (s.pt08_s1_co, 'pt08_s1_co'),
    (s.nmhc_gt, 'nmhc_gt'),
    (s.c6h6_gt, 'c6h6_gt'),
    (s.pt08_s2_nmhc, 'pt08_s2_nmhc'),
    (s.nox_gt, 'nox_gt'),
    (s.pt08_s3_nox, 'pt08_s3_nox'),
    (s.no2_gt, 'no2_gt'),
    (s.pt08_s4_no2, 'pt08_s4_no2'),
    (s.pt08_s5_o3, 'pt08_s5_o3'),
    (s.temperature, 'temperature'),
    (s.rh, 'relative_humidity'),
    (s.ah, 'absolute_humidity')
) AS v(val, code)
JOIN sensor_types st ON st.code = v.code
WHERE v.val IS NOT NULL AND v.val <> '' AND v.val <> '-200';

COMMIT;
