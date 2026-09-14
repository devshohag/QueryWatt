# Report contract

Both `baseline` and `verify` accept `--format console`, `--format json`, or
`--format markdown`. Console is intended for local use, JSON for automation,
and Markdown for CI job summaries or future pull-request comments.

```powershell
.\.tools\querywatt verify .\samples\querywatt.yml --format console
.\.tools\querywatt verify .\samples\querywatt.yml --format json |
    Out-File .\verify-result.json -Encoding utf8
.\.tools\querywatt verify .\samples\querywatt.yml --format markdown |
    Out-File .\verify-result.md -Encoding utf8
```

Verification reports contain the overall verdict and exit code, then one block
per query. Each query includes:

- the regression verdict and whether its estimated plan hash changed;
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
