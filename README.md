# QueryWatt

> Catch SQL Server efficiency regressions before they reach production.

QueryWatt is an early-stage .NET 10 tool for repeatable SQL Server query
measurements. The current milestone implements the load-bearing measurement
core: sequential warm-up/measured execution, complete result draining, capture
of `SET STATISTICS IO, TIME` messages, and raw per-run metrics.

## Current status

Week 4 development milestone. The repository now has strict YAML configuration,
sequential multi-query sessions, fixed typed parameters, transparent R-7/IQR
statistics, schema-versioned baseline JSON, environment fingerprint refusal,
dual-threshold verification, distinct exit codes, and non-measured SHOWPLAN
fingerprints. It also includes three report formats and an opt-in, documented CPU-coefficient
energy model. `QueryWatt.Cli` exposes the intended three commands and packs as a
local .NET tool; no package has been published yet.

## What QueryWatt does not measure

- It does not measure hardware energy use.
- It does not calculate carbon or CO2 emissions.
- Logical reads are primarily buffer-pool activity, not storage energy.
- CPU and duration on shared CI runners are noisy and are not default gates.

## Build and test

Prerequisites: .NET 10 SDK. Live validation additionally requires SQL Server
2022.

```powershell
dotnet restore .\QueryWatt.slnx
dotnet build .\QueryWatt.slnx --configuration Release --no-restore
dotnet test .\QueryWatt.slnx --configuration Release --no-build
```

The internal `samples/QueryWatt.MeasurementProbe` project is the temporary
manual-validation entry point. It is not a fourth public QueryWatt command.
See [`docs/validation.md`](docs/validation.md) for the SQL Server comparison
procedure.

## Public commands

| Command | Purpose |
|---|---|
| `querywatt init [directory]` | Create a non-destructive runnable scaffold. |
| `querywatt baseline [config]` | Measure and write the reviewable baseline JSON. |
| `querywatt verify [config]` | Re-measure, compare, report, and return exit code 0/1/2/3. |

`init` refuses to overwrite any scaffold file that already exists. The two
measurement commands default to `querywatt.yml` and console output.

## Local tool smoke test

Pack and install the tool into a repository-local directory:

```powershell
dotnet pack `
    .\src\QueryWatt.Cli\QueryWatt.Cli.csproj `
    --configuration Release `
    --output .\artifacts

dotnet tool install `
    --tool-path .\.tools `
    --add-source .\artifacts `
    QueryWatt `
    --version 0.4.0-preview.1
```

Run baseline and verification in all three report formats:

```powershell
.\.tools\querywatt baseline .\samples\querywatt.yml
.\.tools\querywatt verify .\samples\querywatt.yml --format console
.\.tools\querywatt verify .\samples\querywatt.yml --format json
.\.tools\querywatt verify .\samples\querywatt.yml --format markdown
```

The YAML path is the source of truth: every query file path is resolved relative
to that YAML file, queries run sequentially, and parameter values use a declared
database type.
See [`docs/configuration.md`](docs/configuration.md) for the schema and exact
statistics behavior, and
[`docs/baseline-and-verify.md`](docs/baseline-and-verify.md) for Week 3 behavior.
The console, JSON, and Markdown contracts are documented in
[`docs/reports.md`](docs/reports.md).
The sustainability methodology and limitations are in
[`docs/energy-model.md`](docs/energy-model.md).

This repository contains synthetic examples only. Never commit client schemas,
queries, execution plans, statistics, connection strings, or production data.
