# QueryWatt — Measurement Contract v2 (in-app measurement)

**Status:** frozen before any in-app measurement code is written. PR #5.
**Scope:** this document adds a second measurement mode. It does not replace
`measurement-contract.md` (v1), which continues to govern the CLI runner
unchanged.
**Rule:** if a decision here changes, the baseline schema version changes with
it. Old baselines are then refused, never silently compared.

---

## 0. What v2 adds

v1 measures queries that QueryWatt itself executes: the tool owns the
connection, the `SET` options, the parameter set and the run count.

v2 adds **in-app measurement**: the developer wraps an existing method in a
measurement scope, the application executes its own unchanged query, and
QueryWatt records what that execution cost.

```csharp
using var m = Watt.Measure("orders.pending-by-customer");

// existing SQL / stored procedure / Dapper / EF Core / NHibernate code,
// unchanged

m.Complete();
```

The measurement block is the **only** intentional source-code addition.
QueryWatt does not rewrite SQL, does not change a return type, does not turn a
`DataTable` into a `DataSet`, and does not add a result set to an existing
result stream.

---

## 1. Two modes, one pipeline

| | **Runner mode** (v1) | **In-app mode** (v2) |
|---|---|---|
| Who executes the query | QueryWatt | the application |
| Who owns the connection | QueryWatt | the application |
| `SET` options | pinned by QueryWatt (v1 §2.1) | inherited from the application |
| Pooling | disabled | whatever the app uses |
| Parameter set | one fixed set per query | whatever the app passed |
| Run count | `measuredRuns` from config | one run per scope execution |
| `source` in baseline | `cli-sql` | `adonet` / `dapper` / `efcore` / `nhibernate` |

Both modes emit the same `RunMetrics`. Everything downstream — statistics,
baseline, thresholds, verdicts, reports — is shared and unchanged. One scope
execution produces exactly **one** `RunMetrics`; N executions of the same
`queryId` + `scenarioKey` aggregate into one `QueryMeasurementSample`.

### 1.1 Cross-mode comparison is forbidden

A baseline entry records its `source`. `verify` refuses to compare entries whose
`source` differs, and a single baseline entry never mixes sources.

This is not tidiness. v1 §2.1 makes the pinned `SET` options part of the
measurement identity, because they change the cached plan and therefore the plan
hash. In-app mode cannot pin them — the application owns the session. Numbers
from the two modes are therefore not measuring the same thing, and the tool says
so instead of averaging them.

### 1.2 v1 clauses that do not apply in-app

| v1 clause | In-app status |
|---|---|
| §2 step 1 — dedicated connection, `Pooling=false` | **not applicable.** The app's connection and pool are used as-is. |
| §2.1 — pinned `SET` options | **not applied.** Inherited; recorded in the environment fingerprint (§10) instead. |
| §2.2 — seed verification before the session | **replaced** by the dataset fingerprint (§10). |
| §2 step 4 — warm-up runs | **caller's responsibility.** `QueryWatt.Testing` supplies a runner that performs warm-up; a bare app run has no warm-up and is marked as such. |
| §2 step 6 — separate plan-capture run | **retained, but off the app's connection** (§8). |

Everything else in v1 — metric definitions, aggregation rules, CPU resolution
floor, duration definition, what the tool refuses to claim — applies to both
modes without change.

---

## 2. Capture mechanism

**Decision: `DiagnosticSource`.** QueryWatt subscribes to the SqlClient
diagnostic listener and observes commands as the application executes them. No
wrapped connection, no wrapped `DbProviderFactory`, no change to how the
application creates commands.

Two listener names are subscribed, because the driver differs by stack:

| Listener | Emitted by |
|---|---|
| `SqlClientDiagnosticListener` — `Microsoft.Data.SqlClient.WriteCommand{Before,After,Error}` | Microsoft.Data.SqlClient (ADO.NET, Dapper, EF Core, modern NHibernate drivers) |
| `SqlClientDiagnosticListener` — `System.Data.SqlClient.WriteCommand{Before,After,Error}` | System.Data.SqlClient (older NHibernate drivers, legacy code) |

Subscription happens **only** when measurement is enabled (§13). When disabled,
no observer is registered and no event payload is read.

The active scope is found through `AsyncLocal<MeasurementScope?>`. The
diagnostic callback runs on the caller's execution context, so a command
executed inside a scope is attributed to that scope, and a command executed
outside every scope is ignored entirely. **QueryWatt never guesses which queries
matter.**

### 2.1 Fallback

`m.Attach(command)` attributes a command explicitly. It exists for providers
that do not emit diagnostic events and for tests. It is documented as a fallback,
never as the standard pattern.

### 2.2 What is captured from the command

`CommandText`, `CommandType`, the command timeout, and for each parameter its
**name, `DbType`, size, precision and scale**. Parameter **values are never
read** — see §14.

---

## 3. Scope lifecycle

```
Measure(queryId)  →  [app executes]  →  Complete()      →  Completed
                                     →  Fail(exception)  →  Failed
                                     →  OperationCanceled→  Cancelled
                                     →  Dispose() only    →  Abandoned
```

| Status | Meaning | Enters baseline? |
|---|---|---|
| `Completed` | `Complete()` was called; every command that started also finished | **yes** |
| `Failed` | `Fail(ex)` was called, or an exception escaped the scope | no — reported, never baselined |
| `Cancelled` | the operation was cancelled | no |
| `Abandoned` | disposed without `Complete()`/`Fail()`, or a command started and never finished | no — reported with a warning |

Rules:

- `Dispose()` is a **fallback**, not a second way to complete. A scope disposed
  without a terminal call becomes `Abandoned`. It is never silently treated as a
  success, because a silently-passing measurement hides a regression.
- `Fail()` never throws. A measurement fault must not change application
  behaviour.
- `Complete()` is idempotent; a second call is ignored.
- Calling `Complete()` while a `DataReader` opened inside the scope is still
  open yields status `Completed` **with an `OpenReaderAtComplete` diagnostic**
  (§17), not a failure — closing a reader after `Complete()` is a legitimate
  pattern. The metrics for that command may be incomplete, and the diagnostic
  says so.

### 3.1 Materialisation

Metrics are only meaningful once the query has materialised. A scope that ends
with `started > finished` commands is `Abandoned`. A method that returns an
unmaterialised `IQueryable`, or a Dapper query with `buffered: false`, must
place `Complete()` after materialisation or accept the diagnostic.

---

## 4. Nesting

Nested scopes are **supported**, not rejected. A repository method that is
measured, called from a service method that is also measured, is normal in
layered code; rejecting it would make developers delete the blocks.

- The inner scope becomes a **child** of the outer scope.
- A command is attributed to the **innermost** active scope.
- A child keeps its own identity, metrics and verdict.
- A parent records its own directly-executed commands plus an aggregate of its
  children, and reports a per-child breakdown.
- Recursion depth is capped (default 16); beyond the cap, further scopes attach
  to the deepest accepted scope and a diagnostic is raised.

---

## 5. Metric acquisition

| Metric | Source | Runner | In-app full | In-app light |
|---|---|---|---|---|
| Client duration | diagnostic before/after timestamps | ✔ | ✔ | ✔ |
| Command identity, type, parameter metadata | diagnostic payload | ✔ | ✔ | ✔ |
| Rows returned, server round trips, bytes | `SqlConnection.RetrieveStatistics()` delta | ✔ | ✔ | ✔ |
| Call count per scope (N+1 signal) | scope bookkeeping | — | ✔ | ✔ |
| Logical reads, LOB reads, physical reads | `SET STATISTICS IO` → `InfoMessage` | ✔ | ✔ | **null** |
| CPU time, server elapsed | `SET STATISTICS TIME` → `InfoMessage` | ✔ | ✔ | **null** |
| Plan fingerprint | sidecar `SHOWPLAN_XML` (§8) | ✔ | ✔ | **null** |
| Exception, error number, timeout | diagnostic error payload | ✔ | ✔ | ✔ |

A metric that was not acquired is `null`. **It is never estimated, defaulted or
inferred.** A verdict is never issued on a null metric.

### 5.1 `STATISTICS IO/TIME` does not alter the result stream

Their output arrives on `SqlConnection.InfoMessage`, outside the result stream.
A `DataSet` filled while they are on has the same `Tables.Count`; a
`DataReader` sees the same result sets; ordinal-based code is unaffected. This is
the reason in-app measurement does **not** need to re-execute the query.

**Re-execution of the application's command is forbidden in all modes and for
all metrics.** A command that writes would run twice.

---

## 6. Connection instrumentation

When instrumentation is `full`, the first command observed on a connection
triggers, on that same connection, before the command executes:

1. `SqlConnection.StatisticsEnabled = true`
2. an `InfoMessage` handler attached
3. `SET STATISTICS IO ON; SET STATISTICS TIME ON`

Notes that constrain the implementation:

- **Pooled reuse resets `SET` options.** Microsoft.Data.SqlClient issues
  `sp_reset_connection` when a pooled connection is reused, which resets `SET`
  state and session language. Instrumentation is therefore applied **per logical
  open**, tracked against the connection's current open generation — not once
  per physical connection. This also bounds the leak: QueryWatt's `SET` state
  cannot outlive the connection's return to the pool.
- **Session language is checked, never changed.** `STATISTICS IO` output is
  localised. QueryWatt reads `@@LANGID` once per instrumented open. If it is not
  `us_english`, QueryWatt **leaves it alone**, reports reads and CPU as `null`,
  and raises the `NonEnglishSessionLanguage` diagnostic naming the fix (set the
  login's default language for measurement runs). QueryWatt never issues
  `SET LANGUAGE` on a connection it does not own: that would change date
  interpretation for application code sharing the pool.
- Messages are flushed to the owning command when the command completes and
  again when its reader closes.
- One connection executes one command at a time, so message attribution is
  unambiguous. If MARS is detected, reads and CPU are reported as `null` with
  the `MarsAttributionUnsafe` diagnostic.

---

## 7. Plan capture

Plans are captured on a **separate QueryWatt-owned connection**, after the
measured execution, using `SET SHOWPLAN_XML ON` — which compiles without
executing. The fingerprint is a hash of the plan's operator and index structure,
not of its estimated costs.

Forbidden: `SET STATISTICS XML ON` on the application's execution. It returns an
extra result set and would change what the application's own code receives.

Plan capture is **skipped**, and `planFingerprint` is `null`, when:

- `CommandType` is `StoredProcedure` — the body is not available to compile
  safely, and a procedure may write;
- the command text parses as anything other than a single read statement
  (conservative: any `INSERT`/`UPDATE`/`DELETE`/`MERGE`/`EXEC`/DDL token, or a
  parse failure, skips capture);
- instrumentation is not `full`.

A skipped plan is stated in the report as skipped, with the reason. It is never
reported as "unchanged".

---

## 8. Query identity

An entry is identified by **`queryId` + `scenarioKey` + `source`**.

- `queryId` is the string the developer passed to `Measure`. It is stable across
  refactors by design — the developer owns it. Duplicate `queryId` +
  `scenarioKey` + `source` within one session is an error, reported with both
  call sites where available.
- `normalizedQueryHash` is recorded for information: command text with literals
  and whitespace normalised. It is **not** part of identity, because an ORM may
  legitimately change generated SQL without changing the operation.

### 8.1 `scenarioKey`

Derived automatically from the **parameter shape**, not parameter values:

```
p[CustomerId:uniqueidentifier, PlacedAfter:datetime2]
```

- parameters whose value is `null` or `DBNull` are **excluded** — a query called
  with two filters and the same query called with four filters are different
  scenarios, and are never compared with each other;
- names are sorted ordinal-ignore-case so ordering is irrelevant;
- `scenarioMode` config: `auto` (default, as above), `explicit`
  (`m.Scenario("name")` only), `single` (one entry per `queryId`; comparison then
  uses reads-per-row only).

A `scenarioKey` with no baseline yields the verdict `NewScenario` — **never a
failure**. `querywatt baseline --accept-new` adds it as its own entry; existing
entries are untouched.

---

## 9. Comparability

Two fingerprints are recorded with every entry and compared before any verdict:

**`environmentFingerprint`** — SQL Server product version, edition,
database compatibility level, effective MAXDOP, and (in-app only) the observed
`SET` options of the measured session.

**`datasetFingerprint`** — for every table the captured plan references: row
count from `sys.dm_db_partition_stats`, plus a hash of that table's index
definitions.

| Fingerprints | Behaviour |
|---|---|
| both match | full verdict (§10) |
| environment differs | no verdict — `Incomparable`, with the differing field named |
| dataset differs | degraded verdict: plan change and reads-per-row only; raw reads, CPU and duration are reported but never fail |

Naming a difference is mandatory. "Incomparable" without a reason is not an
acceptable report line.

---

## 10. Verdict matrix

Rows are compared within `rowsTolerancePercent` (default 10).

| Plan fingerprint | Rows | Cost | Verdict |
|---|---|---|---|
| unchanged | within tolerance | over threshold | `Regression` |
| unchanged | within tolerance | within threshold | `Unchanged` |
| unchanged | grew | grew proportionally (reads/row within threshold) | `DataChange` |
| unchanged | grew | reads/row over threshold | `Suspect` |
| changed | within tolerance | worse | `PlanRegression` |
| changed | within tolerance | better | `Improved` |
| unchanged | within tolerance | better than threshold | `Improved` |
| plan skipped | — | over threshold | `Regression`, plan noted as skipped |
| new `scenarioKey` | — | — | `NewScenario` |
| fingerprint mismatch | — | — | `Incomparable` |

Primary comparison metric is **reads per row**, not raw logical reads. Duration
alone never fails a build. `failOnPlanChange` (default `true`) governs the
`changed` rows.

Exit codes: `0` nothing blocking · `1` `Regression` or `PlanRegression` ·
`2` configuration or baseline error · `3` measurement could not be taken.
`Suspect`, `DataChange`, `NewScenario` and `Incomparable` do not fail a build by
default.

---

## 11. Runs and statistics

v1 §4 statistics rules apply unchanged: median below ~50 samples, p95 above,
IQR trimming, CPU resolution floor.

In-app mode, one scope execution is one run, so the caller decides the sample
size:

- `QueryWatt.Testing` provides `Scenario.Run(times: 30, warmup: 5)`; warm-up
  runs are discarded as in v1.
- A baseline written from fewer than 20 measured runs is marked
  `lowConfidence: true` and `verify` widens its thresholds for that entry
  instead of pretending to precision it does not have.
- A baseline is **never** written from a sample marked `observeOnly` (§12).

---

## 12. Enablement and production

| Level | `Enabled` | Connection instrumentation | Plan sidecar | Baseline writes |
|---|---|---|---|---|
| `off` (default) | no | — | — | — |
| `light` | yes | **no** | no | no — `observeOnly` |
| `full` | yes | yes | yes | yes |

- Default is `off`. `Measure` then returns a no-op `readonly struct`: no
  allocation, no listener, no server interaction. The block is free to ship.
- `Environment: "production"` in configuration makes `baseline` and `verify`
  **refuse to run**, with an explicit error. Never a guess, never a warning that
  proceeds anyway.
- `full` together with `Environment: "production"` logs one startup warning and
  **still does not enable connection instrumentation**.
- `light` supports `samplingRate`. `full` does not sample: a partial sample is
  not a baseline.

---

## 13. Sensitive values

Unchanged from v1 in spirit, restated because in-app mode sees real production
parameters:

- Parameter **values are never read, stored, logged or hashed.** Only name,
  `DbType`, size, precision, scale.
- Command text is stored with literals normalised. A command whose text carries
  inline values (string-concatenated SQL) is flagged `InlineLiteralsDetected`
  (§17) and its text is stored normalised only.
- Connection strings, credentials and server names are not written to a
  baseline or a report.
- Report and baseline files are written under `querywatt/` in the repository and
  are intended to be committed; nothing in them may be sensitive by
  construction, not by redaction afterwards.

---

## 14. Baseline schema v2

Schema version `2`. A v1 baseline is **refused with an explicit message**, and
`querywatt baseline --migrate` rewrites it; it is never silently reinterpreted.

Added per entry: `source`, `scenarioKey`, `parameters[]` (name/type only),
`planFingerprint`, `environmentFingerprint`, `datasetFingerprint`,
`readsPerRow`, `sampleCount`, `lowConfidence`, `observeOnly`.

---

## 15. Public API surface

**Naming decision.** The literal call `QueryWatt.Measure("…")` is not
achievable: a type named `QueryWatt` cannot coexist with the `QueryWatt.*`
namespaces the repository already uses — the name would bind to the namespace.
The facade is therefore:

```csharp
using QueryWatt;

using var m = Watt.Measure("orders.pending-by-customer");
m.Complete();
```

`Watt` is the static facade in namespace `QueryWatt` (project
`QueryWatt.Core`). `QueryWattScope.Begin(...)` is provided as an explicit
alias for codebases that prefer a fully-spelled call.

Surface frozen for v0.6:

```csharp
public static class Watt
{
    public static bool Enabled { get; set; }                  // default false
    public static InstrumentationLevel Instrumentation { get; set; }
    public static MeasurementScope Measure(string queryId);
}

public readonly struct MeasurementScope : IDisposable
{
    public void Complete();
    public void Complete(long rowsReturned);                  // caller-supplied rows
    public void Fail(Exception exception);
    public MeasurementScope Scenario(string name);
    public void Attach(DbCommand command);                    // fallback, §2.1
    public MeasurementRecord? Record { get; }                 // null when disabled
}
```

---

## 16. Diagnostics

Every diagnostic has a stable code, appears in the console and HTML report, and
never throws:

| Code | Meaning |
|---|---|
| `NoCommandsInScope` | a scope completed without executing any command — the block is probably in the wrong method |
| `OpenReaderAtComplete` | `Complete()` called with a reader from this scope still open |
| `AbandonedScope` | disposed without a terminal call, or a command never finished |
| `DuplicateQueryId` | same `queryId` + `scenarioKey` + `source` twice in one session |
| `NonEnglishSessionLanguage` | reads and CPU unavailable; session language is not `us_english` |
| `MarsAttributionUnsafe` | multiple active result sets on one connection |
| `InlineLiteralsDetected` | command text contains inline literals; identity may be unstable |
| `PlanCaptureSkipped` | with the reason from §7 |
| `LowConfidenceSample` | fewer than 20 measured runs |
| `NestingDepthExceeded` | scope depth cap reached |

---

## 17. Explicitly undecided in v2

Written down so they are not quietly decided by accident:

- Adapter-specific metadata — EF Core LINQ source and split-query grouping,
  NHibernate HQL/Criteria source, Dapper multi-mapping. v0.6 captures metrics for
  these stacks through the shared mechanism; the enrichment packages come later.
- Automatic N+1 threshold tuning. v0.6 uses a fixed call-count threshold per
  scope (default 5) and reports; it never fails a build on it.
- Cross-machine aggregation of reports. The HTML report is per-machine, one file.
- EF6, PostgreSQL, Oracle.
- Async-local flow across `Task.Run` boundaries that discard the execution
  context: commands executed there are attributed to no scope and ignored.
