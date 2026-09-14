# QueryWatt

> Catch SQL Server efficiency regressions before they reach production.

QueryWatt is an early-stage .NET 10 tool for repeatable SQL Server query
measurements. The current milestone implements the load-bearing measurement
core: sequential warm-up/measured execution, complete result draining, capture
of `SET STATISTICS IO, TIME` messages, and raw per-run metrics.

## Current status

Week 2 development milestone. The measurement core has passed its manual
three-way validation. The repository now adds strict YAML configuration,
sequential multi-query sessions, fixed typed parameters, and transparent
R-7/IQR statistics. The public `init`, `baseline`, and `verify` commands remain
intentionally unexposed until their contracts are implemented.

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

For the temporary Week 2 multi-query probe:

```powershell
dotnet run `
    --project .\samples\QueryWatt.MeasurementProbe `
    --configuration Release `
    --no-build `
    -- .\samples\querywatt.yml |
    Out-File .\week2-result.json -Encoding utf8
```

The YAML path is the source of truth: every query file path is resolved relative
to that YAML file, and queries run sequentially in listed order. Parameter values
are parsed with invariant culture and a declared database type.
See [`docs/configuration.md`](docs/configuration.md) for the schema and exact
statistics behavior.

This repository contains synthetic examples only. Never commit client schemas,
queries, execution plans, statistics, connection strings, or production data.
