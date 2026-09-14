# Report contract

Both `baseline` and `verify` accept `--format console`, `--format json`, or
`--format markdown`. Console is intended for local use, JSON for automation,
and Markdown for CI job summaries and pull-request comments.

```powershell
.\.tools\querywatt verify .\samples\querywatt.yml --format console
.\.tools\querywatt verify .\samples\querywatt.yml --format json |
    Out-File .\verify-result.json -Encoding utf8
```

## Writing several reports from one measurement

`--format` controls standard output. `--output FORMAT=PATH` additionally writes
the report to a file and may be repeated. Rendering the same result twice is
free; measuring twice is not, so prefer one run with several outputs over two
runs with different formats.

```powershell
.\.tools\querywatt verify .\samples\querywatt.yml `
    --format console `
    --output markdown=artifacts/querywatt-report.md `
    --output json=artifacts/querywatt-report.json
```

Missing directories are created. An `--output` value that is not `FORMAT=PATH`,
or that names an unsupported format, fails with exit code 3 before any query is
measured.

## Verdicts

| Verdict | Exit code | Meaning |
|---|---|---|
| `passed` | 0 | Every gated metric stayed inside its thresholds. |
| `regressed` | 1 | At least one gated metric exceeded both its percent and absolute threshold. |
| `baseline-stale` | 2 | The seed or schema changed, so the stored numbers describe a different database. No comparison was attempted; run `querywatt baseline` again and commit the new file. |

A `baseline-stale` result still produces a full report carrying the explanation,
rather than only an error on standard error.

## Warnings

Reports carry a `warnings` list for conditions that did not stop the comparison
but that a reviewer should see — for example a SQL Server cumulative update that
moved the build number while leaving the major version alone. Warnings never
change the exit code.

## Per-query content

Each query block contains:

- the regression verdict for that query;
- **query text changed** and **estimated plan shape changed** as two separate
  signals. The query text changes in most pull requests this tool is meant to
  review, so a combined flag would carry no information; the plan shape is the
  part worth attention;
- baseline/current IQR-filtered medians, absolute and percent change;
- configured thresholds, outlier counts, and p95 when at least 50 runs exist;
- measured CPU-core-seconds as a transparent resource index;
- optional estimated Wh values in a separately labelled block.

The JSON property names are camel-case. A percent change whose baseline is zero
is `null`, never infinity or a fabricated percentage. Informational metrics
remain visible even when they cannot fail verification.

Energy values are absent unless the user opts in with a coefficient. Reports
never label energy as measured and never output carbon. See
[`energy-model.md`](energy-model.md).
