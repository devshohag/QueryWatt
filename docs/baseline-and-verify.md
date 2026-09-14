# Baseline and verification contract

The baseline and verify workflow is exposed by the Week 4 `querywatt` CLI. The
sample measurement probe remains available only for low-level validation.

## Baseline

```powershell
.\.tools\querywatt baseline .\samples\querywatt.yml --format console
```

The configured `baselineFile` is written atomically with stable camel-case key
ordering. It contains schema/tool versions, the environment fingerprint,
approved thresholds, query and parameter hashes, estimated SHOWPLAN statement
hashes, full summaries, and raw per-run values with statement/table breakdowns.
It intentionally has no generated timestamp, so re-baselining unchanged data
does not create a meaningless timestamp-only diff.

## Verify

```powershell
.\.tools\querywatt verify .\samples\querywatt.yml --format console
```

Comparison uses the IQR-filtered median. Logical reads gate by default at more
than 25 percent **and** more than 1,000 pages above baseline. Equality with a
threshold does not fail. CPU can gate only when explicitly configured and the
baseline median is at least 10 ms. Duration can gate only when configured.
The synthetic seek sample intentionally overrides the absolute threshold to 50
pages so a non-sargable rewrite can demonstrate exit code 1 on the small data
set.

An estimated plan-hash change is reported independently and never fails v1 by
itself. Plan capture is one final `SET SHOWPLAN_XML ON` execution-planning step;
it is outside all measured runs and does not execute the query.

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | All configured queries are within approved thresholds. |
| 1 | At least one measured regression exceeded both thresholds. |
| 2 | Measurement failed or the measurement environment/fingerprint differs. |
| 3 | Usage, YAML, baseline-file, query-set, or threshold-policy error. |

Console, JSON, and Markdown output are selected with `--format`. See
[`reports.md`](reports.md) for the output contract.
