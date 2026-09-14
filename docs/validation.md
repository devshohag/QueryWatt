# Measurement Validation

**Status:** SQL Server IO parsing and primary logical-read aggregation validated
manually on 2026-09-14. A clean independent client-duration comparison for all
three shapes remains a documentation follow-up; SSMS Live Query Statistics is
not a valid wall-clock comparator.

The sample database contained 50,000 deterministic synthetic `Ticket` rows.
Every QueryWatt result below used three discarded warm-ups followed by twenty
measured runs, pooling disabled, and the pinned `SET` options in the measurement
contract.

## Recorded observations

| Shape | QueryWatt observations | Matching SSMS observation | Result |
|---|---|---|---|
| Indexed seek | 20/20 runs: 2 logical reads, 50 rows, 0 ms CPU. Client duration 0.3643–1.5873 ms. | 2 logical reads, 50 rows, 0 ms CPU. | Primary metric exact. |
| Non-sargable scan | 20/20 runs: 132 logical reads, 1,440 rows. CPU 0–16 ms; client duration 10.3118–15.9754 ms. | 132 logical reads and 1,440 rows. The displayed 69 ms elapsed run had Live Query Statistics enabled and is excluded from duration validation. | Primary metric exact. |
| Forced sort/worktable candidate | 20/20 runs: `Ticket` 1,395 logical reads; `Worktable` logical reads 0 and read-ahead captured. CPU 1,813–1,969 ms; client duration 1,926.9858–2,078.1758 ms. | `Ticket` 1,395 logical reads; `Worktable` 0 logical reads and 2,649 read-ahead reads; CPU 1,922 ms, elapsed 2,006 ms. | Primary metric exact; CPU and duration inside measured spread. |

The worktable read-ahead values varied across later probe runs (3,656, 3,670,
and 3,672), which is why read-ahead remains informational in v1.

## Parser defect found by validation

The first parser regex matched `read-ahead reads` inside the longer SQL Server
metric name `page server read-ahead reads`. A later zero value therefore
overwrote the real Worktable read-ahead value. The parser now matches longer
page-server keys before ordinary read-ahead keys, and a regression test retains
the exact SQL Server message that exposed the defect.

## Local reproduction

Run `samples/TicketingDatabase/setup.sql` in SSMS, then:

```powershell
$env:QUERYWATT_CONNECTION_STRING = "Server=localhost;Database=QueryWattSample;Integrated Security=True;TrustServerCertificate=True"

dotnet run `
    --project .\samples\QueryWatt.MeasurementProbe `
    --configuration Release `
    -- .\samples\querywatt.yml |
    Out-File .\week2-result.json -Encoding utf8
```

For an independent duration check, disable Actual Execution Plan and Live Query
Statistics, drain the complete result, and record client wall-clock timing. Do
not compare QueryWatt's client duration to SSMS's UI duration when either visual
plan feature is enabled.
