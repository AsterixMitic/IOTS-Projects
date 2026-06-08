# Benchmark Results (k6)

Generated on: 2026-05-18

## Source artifacts

- `load-tests/results/01-ingestion-summary.json`
- `load-tests/results/02-selective-monitoring-summary.json`
- `load-tests/results/03-historical-aggregation-summary.json`

## Scenario summary

| Script | Iterations | Check pass rate | HTTP avg (ms) | HTTP p95 (ms) | gRPC avg (ms) | gRPC p95 (ms) | HTTP fail rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| `01-ingestion.js` | 3560 | 20.14% | 64.25 | 18.56 | 6.08 | 7.54 | 49.09% |
| `02-selective-monitoring.js` | 4502 | 33.33% | 8.30 | 13.86 | 4.59 | 5.74 | 50.00% |
| `03-historical-aggregation.js` | 2703 | 33.33% | 9.33 | 17.17 | 5.18 | 6.87 | 50.00% |

## Check breakdown

### 01-ingestion

| Check | Pass | Fail |
|---|---:|---:|
| GraphQL ingestion accepted | 1201 | 0 |
| GraphQL returned data | 0 | 1201 |
| gRPC ingestion accepted | 0 | 1201 |
| gRPC returned id | 0 | 1201 |
| REST ingestion accepted | 0 | 1158 |

### 02-selective-monitoring

| Check | Pass | Fail |
|---|---:|---:|
| GraphQL selective monitoring accepted | 1501 | 0 |
| GraphQL returned readings array | 0 | 1501 |
| gRPC selective monitoring accepted | 0 | 1500 |
| gRPC returned readings array | 1500 | 0 |
| REST selective monitoring accepted | 0 | 1501 |
| REST returned readings array | 0 | 1501 |

### 03-historical-aggregation

| Check | Pass | Fail |
|---|---:|---:|
| GraphQL historical aggregation accepted | 901 | 0 |
| GraphQL returned aggregate array | 0 | 901 |
| gRPC historical aggregation accepted | 0 | 901 |
| gRPC returned aggregate array | 901 | 0 |
| REST historical aggregation accepted | 0 | 901 |
| REST returned aggregate array | 0 | 901 |

## Notes

- All three runs completed and produced summary files.
- Threshold `checks: rate>0.99` was crossed in each run due to failed checks above.
