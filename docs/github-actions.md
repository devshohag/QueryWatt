# GitHub Actions integration

QueryWatt ships a Linux composite action and a complete SQL Server
service-container workflow. The repository workflow measures the approved base
branch and the proposed pull-request query **in the same job, against the same
seeded container**, so the two numbers come from the same machine in the same
minute. Comparing measurements taken on unrelated runners is not sound, and this
avoids it.

## Workflow sequence

1. Check out the proposed commit and the pull request's approved base SHA.
2. Build and test the proposed QueryWatt engine.
3. Start the pinned SQL Server 2022 CU23 Ubuntu 22.04 image.
4. Seed the database from the approved base checkout.
5. Use the proposed engine to create a temporary baseline from base queries.
6. Verify proposed queries against that baseline.
7. Add the Markdown report to the job summary and one updatable PR comment.
8. Upload the reports for 14 days, then enforce the QueryWatt exit code.

The workflow uses `pull_request`, never `pull_request_target`, and does not
expose production credentials. The included SQL password is synthetic and exists
only inside the disposable job. A fork may have a read-only token, so PR-comment
failure is non-blocking; the measurement verdict still gates the job.
Third-party actions are pinned to the full commit SHA of a named release rather
than a mutable branch.

## Composite action inputs

| Input | Default | Meaning |
|---|---|---|
| `configuration-file` | `querywatt.yml` | YAML configuration to verify. |
| `tool-version` | `0.1.0-preview.1` | Exact QueryWatt tool version. |
| `package-source` | NuGet.org | Source containing the tool package. |
| `setup-dotnet` | `true` | Install the .NET SDK first. Set `false` when the job already ran `actions/setup-dotnet`. |
| `dotnet-version` | `10.0.x` | SDK version installed when `setup-dotnet` is on. |
| `report-file` | `artifacts/querywatt-report.md` | Markdown report path. |
| `json-report-file` | *(none)* | Optional JSON report, written from the same measurement. |
| `comment-on-pr` | `true` | Upsert one marker-owned PR comment. |
| `upload-report` | `true` | Retain the reports as a workflow artifact. |
| `fail-on-regression` | `true` | Fail the job on a non-zero QueryWatt exit code. |

## Outputs

| Output | Meaning |
|---|---|
| `exit-code` | `0` pass, `1` regression, `2` not compared, `3` configuration. |
| `verdict` | `passed`, `regressed`, `not-compared`, `configuration-error`. |
| `report-file` | Path to the Markdown report. |
| `json-report-file` | Path to the JSON report, empty when none was requested. |

`not-compared` covers a failed measurement, an environment-fingerprint mismatch
and a stale baseline. The report says which one it was.

## Reading outputs instead of failing

A composite action that fails publishes **no outputs at all**. That is a
GitHub Actions rule, and it is easy to trip over: wrap the action in
`continue-on-error: true` and every real failure arrives downstream as an empty
`exit-code`, with the underlying cause invisible.

Two consequences shape this action:

- Installing the tool never aborts the action. A failed install is captured,
  written into the report, and surfaced as exit code 3, so the caller always
  receives an explanation.
- `fail-on-regression: "false"` keeps the action successful and leaves the
  decision to a later step. This repository's own workflow uses that, then
  enforces the exit code itself. Use the default `true` when you just want the
  check to go red.

```yaml
- name: QueryWatt
  id: querywatt
  uses: devshohag/QueryWatt@v0.1.0-preview.1
  with:
    configuration-file: querywatt.yml
    fail-on-regression: "false"

- name: Decide
  run: |
    echo "verdict: ${{ steps.querywatt.outputs.verdict }}"
    exit ${{ steps.querywatt.outputs.exit-code }}
```

## Permissions

`contents: read` is enough for measurement and the job summary. Add
`pull-requests: write` only if you enable `comment-on-pr`; without it the
comment step is skipped without failing the job.

## Prerequisites

A SQL Server connection string must be present in the environment variable named
by the YAML. The action installs the .NET SDK by default, so a bare
`ubuntu-latest` runner is enough. Until QueryWatt is published to NuGet.org, the
sample workflow packs the proposed source and supplies `./artifacts` as a local
package source.

## Current boundary

This workflow validates query-only changes. If the seed script changes,
QueryWatt returns the `baseline-stale` verdict with a report explaining that the
stored numbers describe a different database, rather than pretending the
measurements are comparable. Schema-change comparison needs an explicit future
migration protocol.
