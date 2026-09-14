# GitHub Actions integration

Week 5 adds a Linux composite action and a complete SQL Server service-container
workflow. The repository workflow measures the approved base branch and the
proposed pull-request query in the same job, against the same seeded container.
This avoids comparing measurements from unrelated runners.

## Workflow sequence

1. Check out the proposed commit and the pull request's approved base SHA.
2. Build and test the proposed QueryWatt engine.
3. Start the pinned SQL Server 2022 CU23 Ubuntu 22.04 image.
4. Seed the database from the approved base checkout.
5. Use the proposed engine to create a temporary baseline from base queries.
6. Verify proposed queries against that baseline.
7. Add the Markdown report to the job summary and one updatable PR comment.
8. Upload the report for 14 days, then enforce QueryWatt exit code 0/1/2/3.

The workflow uses `pull_request`, never `pull_request_target`, and does not expose
production credentials. The included SQL password is synthetic and exists only
inside the disposable job. A fork may have a read-only token, so PR-comment
failure is non-blocking; the measurement verdict still gates the job.
Third-party workflow actions are pinned to the full commit SHA of a named
release rather than a mutable branch.

## Composite action inputs

| Input | Default | Meaning |
|---|---|---|
| `configuration-file` | `querywatt.yml` | YAML configuration to verify. |
| `tool-version` | `0.5.0-preview.1` | Exact QueryWatt tool version. |
| `package-source` | NuGet.org | Source containing the tool package. |
| `report-file` | `artifacts/querywatt-report.md` | Generated Markdown report. |
| `comment-on-pr` | `true` | Upsert one marker-owned PR comment. |
| `upload-report` | `true` | Retain the Markdown workflow artifact. |

The action expects .NET to be installed and a SQL Server connection string in
the environment variable named by the YAML. Until QueryWatt is published, the
sample workflow packs the proposed source and supplies `./artifacts` as a local
package source.

The base configuration fallback in the workflow exists only to bootstrap the
first Week 5 pull request, whose base branch does not yet contain `samples/ci`.
After Week 5 is merged, every later pull request uses the configuration from its
actual base SHA.

## Current boundary

This workflow validates query-only changes. If the seed script or environment
fingerprint changes, QueryWatt returns exit code 2 instead of pretending the
measurements are comparable. Schema-change comparison needs an explicit future
migration protocol.
