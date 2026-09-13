# QueryWatt

> Catch SQL Server efficiency regressions before they reach production.

QueryWatt is an early-stage .NET 10 tool for repeatable SQL Server query
measurements. The current milestone implements the load-bearing measurement
core: sequential warm-up/measured execution, complete result draining, capture
of `SET STATISTICS IO, TIME` messages, and raw per-run metrics.

## Current status

Week 1 measurement spike. The public `init`, `baseline`, and `verify` commands
are intentionally not exposed until the measurements have passed the manual
three-way validation required by
[`docs/measurement-contract.md`](docs/measurement-contract.md).

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

This repository contains synthetic examples only. Never commit client schemas,
queries, execution plans, statistics, connection strings, or production data.
