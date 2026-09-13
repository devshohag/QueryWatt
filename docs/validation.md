# Measurement Validation

**Status: pending.** The current execution environment does not provide the
.NET 10 SDK or SQL Server, so no measurement claim has been validated yet.

Before the measurement core is marked trusted, run the same pinned SET options,
parameters, and warm state through QueryWatt and SSMS for:

1. an indexed seek;
2. a table or index scan;
3. a query that spills to a worktable.

Record the raw QueryWatt output and the matching SSMS `STATISTICS IO, TIME`
output below. Logical reads must match exactly, CPU must be within one reporting
step, and client duration must fall within the observed run-to-run spread.

## Local probe

Run `samples/TicketingDatabase/setup.sql` in SSMS, then set a local-only
connection string and execute one workload:

```powershell
$env:QUERYWATT_CONNECTION_STRING = "Server=localhost;Database=QueryWattSample;Integrated Security=True;TrustServerCertificate=True"

dotnet run `
    --project .\samples\QueryWatt.MeasurementProbe `
    -- .\samples\TicketingDatabase\queries\seek.sql
```

The worktable query is a spill candidate, not a guarantee: whether it spills
depends on the SQL Server build, statistics, and available memory. Confirm the
actual `Worktable` output in SSMS before recording it as the third validation
case.

| Shape | QueryWatt logical reads | SSMS logical reads | CPU agreement | Duration agreement | Result |
|---|---:|---:|---|---|---|
| Seek | Pending | Pending | Pending | Pending | Pending |
| Scan | Pending | Pending | Pending | Pending | Pending |
| Worktable spill | Pending | Pending | Pending | Pending | Pending |
