# QueryWatt

> Catch SQL Server efficiency regressions before they reach production.

QueryWatt is an early-stage .NET 10 tool for repeatable SQL Server query
measurements. The current milestone implements the load-bearing measurement
core: sequential warm-up/measured execution, complete result draining, capture
of `SET STATISTICS IO, TIME` messages, and raw per-run metrics.

## Current status

Week 3 development milestone. The repository now has strict YAML configuration,
sequential multi-query sessions, fixed typed parameters, transparent R-7/IQR
statistics, schema-versioned baseline JSON, environment fingerprint refusal,
dual-threshold verification, distinct exit codes, and non-measured SHOWPLAN
fingerprints. These are currently exercised through the internal measurement
probe; the packaged public CLI is not released yet.

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

Create a baseline through the temporary development probe:

```powershell
dotnet run `
    --project .\samples\QueryWatt.MeasurementProbe `
    --configuration Release `
    --no-build `
    -- baseline .\samples\querywatt.yml
```

Then verify the unchanged workload:

```powershell
dotnet run `
    --project .\samples\QueryWatt.MeasurementProbe `
    --configuration Release `
    --no-build `
    -- verify .\samples\querywatt.yml
```

The YAML path is the source of truth: every query file path is resolved relative
to that YAML file, queries run sequentially, and parameter values use a declared
database type.
See [`docs/configuration.md`](docs/configuration.md) for the schema and exact
statistics behavior, and
[`docs/baseline-and-verify.md`](docs/baseline-and-verify.md) for Week 3 behavior.

This repository contains synthetic examples only. Never commit client schemas,
queries, execution plans, statistics, connection strings, or production data.
