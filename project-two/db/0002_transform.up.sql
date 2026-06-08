BEGIN;

TRUNCATE reading_values, readings RESTART IDENTITY;

WITH device AS (
  SELECT id AS device_id
  FROM devices
  WHERE external_id = 'air-quality-station-01'
),
staged AS (
  SELECT
    to_date(NULLIF(trim(s.date_col), ''), 'DD/MM/YYYY') AS source_date,
    replace(NULLIF(trim(s.time_col), ''), '.', ':')::time AS source_time,
    (to_timestamp(
      NULLIF(trim(s.date_col), '') || ' ' || replace(NULLIF(trim(s.time_col), ''), '.', ':'),
      'DD/MM/YYYY HH24:MI:SS'
    ))::timestamptz AS recorded_at,
    s.co_gt,
    s.pt08_s1_co,
    s.nmhc_gt,
    s.c6h6_gt,
    s.pt08_s2_nmhc,
    s.nox_gt,
    s.pt08_s3_nox,
    s.no2_gt,
    s.pt08_s4_no2,
    s.pt08_s5_o3,
    s.temperature,
    s.rh,
    s.ah
  FROM staging_airquality s
  WHERE NULLIF(trim(s.date_col), '') IS NOT NULL
    AND NULLIF(trim(s.time_col), '') IS NOT NULL
),
inserted_readings AS (
  INSERT INTO readings (device_id, recorded_at, source_date, source_time, notes)
  SELECT device.device_id,
         staged.recorded_at,
         staged.source_date,
         staged.source_time,
         'Imported from staging'
  FROM staged
  CROSS JOIN device
  RETURNING id, source_date, source_time
)
INSERT INTO reading_values (reading_id, sensor_type_id, numeric_value)
SELECT ir.id, st.id, NULLIF(replace(v.val, ',', '.'), '')::numeric
FROM inserted_readings ir
JOIN staged s
  ON s.source_date = ir.source_date
 AND s.source_time = ir.source_time
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
