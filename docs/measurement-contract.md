# QueryWatt — Measurement Contract

**Status:** v1 draft, frozen before any measurement code is written.
**Rule:** if a decision here changes, the baseline schema version changes with it. Old baselines are then refused, never silently compared.

This document defines exactly what QueryWatt measures, how, and what it refuses to claim. Every disagreement about a number should be resolvable by reading this file.

---

## 0. Terms

| Term | Meaning |
|---|---|
| **Run** | One execution of one registered query with one fixed parameter set. |
| **Warm-up run** | A run whose results are discarded entirely. Never reported, never stored. |
| **Measured run** | A run whose metrics enter the sample. |
| **Sample** | The ordered set of measured runs for one query in one session. |
| **Baseline** | A committed JSON file holding one sample summary per query, plus the environment fingerprint. |
| **Verify session** | A fresh sample compared against a baseline. |

---

## 1. Metrics

### Primary — can fail CI
- **Logical reads** (pages). Chosen because it is deterministic for a given plan and data set, and is the metric least affected by noisy CI hardware.

### Supporting — reported, can fail CI only if explicitly enabled in config
- **CPU time** (ms), subject to the resolution floor in §4.
- **Elapsed duration** (ms), wall clock, measured as defined in §5.

### Informational — reported, never fails CI in v1
- **Rows returned** — a change here means the query's meaning changed, not its cost. Reported so a reviewer notices.
- **Query plan hash** — see §7.
- **Physical reads and read-ahead reads** — captured and stored, but not compared in v1. They are the input for any future storage-energy term (§11) and are far noisier than logical reads.

---

## 2. Execution protocol

Order, per query, per session:

1. Open a dedicated connection with pooling disabled for the session (`Pooling=false`) so connection-level `SET` state cannot leak between queries.
2. Apply the pinned `SET` options from §2.1. **Always, explicitly, never inherited.**
3. Run the seed verification check (§2.2). Abort the whole session on mismatch.
4. Execute `warmupRuns` warm-up runs. Discard everything.
5. Execute `measuredRuns` measured runs back to back, no delay, no interleaving with other queries.
6. Execute one separate plan-capture run (§7). Its timings are discarded.
7. Close the connection.

Queries are measured **sequentially**, never in parallel. Parallel measurement invalidates CPU and duration for every query in the batch.

### 2.1 Pinned SET options

Different `SET` options produce a different cached plan and therefore a different plan hash, so they are part of the measurement identity and are recorded in the baseline fingerprint:

```
SET ANSI_NULLS ON
SET ANSI_PADDING ON
SET ANSI_WARNINGS ON
SET ARITHABORT ON
SET CONCAT_NULL_YIELDS_NULL ON
SET QUOTED_IDENTIFIER ON
SET NUMERIC_ROUNDABORT OFF
SET NOCOUNT OFF          -- required; STATISTICS IO output depends on message flow
SET STATISTICS IO ON
SET STATISTICS TIME ON
```

`ARITHABORT ON` matters in particular: SSMS defaults to `ON` and many application drivers default to `OFF`, which is a classic source of "it's fast in SSMS, slow in the app". QueryWatt pins it so the comparison is honest, and states in the README that it measures the `ARITHABORT ON` plan.

### 2.2 Seed determinism

The baseline stores a **seed fingerprint**: a hash over the ordered list of seed script file contents, plus row counts of every table named in the config. `verify` recomputes it and **refuses to compare** on mismatch. A regression report against a different data set is worse than no report.

---

## 3. Logical reads — aggregation rules

`SET STATISTICS IO` emits one message per table touched, plus separate lines for `Worktable` and `Workfile`. The reported figure is:

**`logicalReads` = sum of the `logical reads` value across every `Table '...'` line emitted for the statement, including `Worktable` and `Workfile`.**

Rationale: worktable and workfile reads are real work caused by the plan — a spilling sort or hash join is exactly the regression we want to catch. Excluding them would hide the most interesting failures.

Additional rules:
- **`lob logical reads` are summed into a separate `lobLogicalReads` field**, reported alongside but not added into `logicalReads`. LOB access has a different cost profile and mixing them makes the primary metric jumpy for no benefit.
- **Per-table breakdown is retained** in the raw record, so a report can say *which* table's reads exploded. This is the single most useful line in a regression report and costs nothing to keep.
- For a stored procedure emitting multiple statements, all statements' messages are summed into the procedure's total, and the per-statement breakdown is retained.
- If the same table name appears more than once (multiple statements, or a self-join reported twice), **all occurrences are summed.** They are not deduplicated.

---

## 4. CPU time — source and resolution floor

- Source: the `SQL Server Execution Times: CPU time = N ms` line from `SET STATISTICS TIME`, summed across all statements of the batch. **Parse time and compile time lines are excluded** — warm-up exists precisely to take compilation out of the measurement.
- **Resolution floor.** `STATISTICS TIME` reports whole milliseconds, so any query under roughly 10 ms CPU quantizes badly and a "+100%" delta can mean 1 ms → 2 ms. Therefore:
  - if the baseline median CPU is `< 10 ms`, CPU is reported with the marker `below-resolution` and **cannot fail CI**, whatever the config says;
  - the report states the floor rather than hiding the metric.
- This floor does **not** apply to logical reads, which are exact counts.

---

## 5. Duration

- **Starts** immediately before `ExecuteReader()`, after the connection is open and the command is prepared.
- **Ends** after the reader is fully drained (§6) and closed.
- Excludes: connection open, `SET` option application, seed verification, plan capture, and all parsing of `InfoMessage` output.
- Measured with `Stopwatch` (monotonic), never `DateTime.Now`.
- Every measured run fully materializes every row of every result set. A query is not measured until its results have actually crossed the wire — otherwise the tool measures how fast SQL Server starts answering, not how expensive the answer is.

---

## 6. Reader drain and InfoMessage ordering

`InfoMessage` events arrive asynchronously during and after execution, and the `STATISTICS IO` / `STATISTICS TIME` lines for the final statement are typically not delivered until the result stream has been consumed.

Contract:

1. Attach the `InfoMessage` handler and clear the message buffer **before** `ExecuteReader()`.
2. Drain every result set: `while (reader.Read()) { }` then `while (reader.NextResult())` and repeat.
3. `reader.Close()`.
4. **Only then** read the accumulated message buffer and parse it.

Reading the buffer before step 3 yields a partial, silently wrong number. This is the single most likely way for QueryWatt to report a confident lie, so the parser asserts that at least one `Execution Times` block was seen and **fails the run** if not.

Parser must tolerate: localized server messages (fail loudly with a clear error rather than mis-parse), multiple statements, repeated table lines, and messages interleaved with user `PRINT` output.

---

## 7. Plan capture

Plan hash is **not** taken from `sys.dm_exec_query_stats`. That path depends on the plan still being cached, on matching the right row, and on `VIEW SERVER PERFORMANCE STATE` permission that a CI service account may not have.

**v1 approach:** a separate, final, non-measured step using `SET SHOWPLAN_XML ON`, which returns the plan **without executing the query**, then reading `QueryHash` and `QueryPlanHash` from the statement element of the returned XML.

Consequences, stated plainly in the report and the docs:
- This is the **estimated** plan for the pinned `SET` options and the given parameter set, not a per-run record of the actual plan used.
- Therefore QueryWatt reports **"plan hash changed between baseline and current"**, and never "the plan changed during the run".
- Adaptive joins, memory-grant feedback, and parameter-sensitive plan variants can make the actual plan differ from this one. v1 does not attempt to track that; `docs/limitations.md` says so.

A plan hash change is reported as its own line with a `changed` marker. In v1 it **never fails CI on its own** — it is context for a human reading a failure, not a verdict.

---

## 8. Statistics

- Warm-up runs are excluded before any statistic is computed.
- **All raw measured values are stored** in the session record before any filtering. Outliers are never silently discarded.
- Reported per metric: `min`, `median`, `max`, `mean`, `stdDev`, `n`, and both **raw** and **IQR-filtered** median, so a reader can see what filtering did.
- IQR filter: values outside `[Q1 − 1.5·IQR, Q3 + 1.5·IQR]` are marked as outliers, retained in the raw array, and excluded from the filtered median. The report prints the count of excluded values.
- Quartiles, median, and p95 use the R-7 linear-interpolation definition (`h = (n - 1)p + 1`). Population standard deviation is reported because the stored runs are the complete measurement session, not a sample used to estimate an unseen session.
- **p95 gating by sample size:**
  - `n < 20` — refuse to produce a summary at all; the run is an error, not a result.
  - `20 ≤ n < 50` — **median only.** p95 is not computed or displayed.
  - `n ≥ 50` — p95 computed and displayed, marked `informational`; it may fail CI only if explicitly enabled.
- Comparison between baseline and current always uses the **filtered median** of the same metric, and the report names which statistic the verdict came from.

---

## 9. Thresholds

- Every metric carries **both** a relative and an absolute threshold. A regression fails only when **both** are exceeded. This is what stops a query that reads 40 pages from failing for reading 60.
- Thresholds are per metric and may be overridden per query.
- A metric with no threshold configured is reported and never fails CI.
- Defaults shipped by `init`: logical reads 25% **and** 1,000 pages. CPU and duration reported only, thresholds commented out in the generated config with a note explaining why (CI noise).

---

## 10. Failure taxonomy and exit codes

A regression and a broken run are different events and must never share an exit code:

| Code | Meaning |
|---|---|
| `0` | All queries within thresholds. |
| `1` | At least one **measured regression** past threshold. This is the PR gate. |
| `2` | **Measurement failure** — query threw, seed fingerprint mismatch, `Execution Times` block missing, `n` below minimum, connection lost. No verdict is produced for the affected query. |
| `3` | **Configuration or usage error** — bad YAML, missing script file, no baseline found. |

A query that fails to execute is reported as `error`, never as `regressed` and never as `passed`. A baseline is never written from a session containing an error.

---

## 11. Energy

**Off by default. No default coefficient ships.**

```yaml
energy:
  enabled: false
  wattsPerBusyCore: null    # no value can be invented on the user's behalf
```

With `enabled: false` or a null coefficient, the report shows the resource index only:

```
CPU energy index: 1,584 CPU-core-seconds/day   (Wh unavailable — no energy profile configured)
```

With a coefficient configured:

```
Energy (Wh) = cpuCoreSeconds × wattsPerBusyCore ÷ 3600
```

Hard rules:
- `wattsPerBusyCore` is **watts per busy core** — a rate. It is never named "watts per core-second".
- Measured values and estimated values never appear in the same block of the report, and the estimated block always carries the model id, the coefficient used, and the word *estimated*.
- **QueryWatt never outputs gCO₂e** in v1. Carbon requires grid intensity, embodied hardware emissions, and a functional unit, none of which this tool has.
- Logical reads are predominantly buffer-pool hits and are **not** a storage-energy proxy. Any future I/O energy term keys off **physical** reads, which is why they are captured now (§1).
- `docs/energy-model.md` states the formula, where a coefficient can be obtained, and what the number does not mean. It ships in the same commit as the feature, not later.

---

## 12. Baseline file contract

- `schemaVersion` is mandatory. A baseline whose version does not match the tool's is **refused**, with a message telling the user to re-baseline. Never auto-migrated in v1.
- The baseline stores an **environment fingerprint**: SQL Server product version and edition, the container image tag if given, the pinned `SET` options hash, the seed fingerprint (§2.2), the tool version, `warmupRuns`, `measuredRuns`, and the parameter set hash per query.
- `verify` compares fingerprints first. On mismatch it **refuses to report deltas** and exits `2`, naming the field that differs. Comparing across SQL Server versions or data sets produces numbers that look authoritative and mean nothing.
- Written with stable key ordering and stable float formatting so a `git diff` shows only what actually changed.
- Raw per-run arrays are included. The file is meant to be reviewable, and a reviewer who cannot see the spread cannot judge the claim.

---

## 13. Validation protocol — do this before writing anything else

The measurement engine is trusted only after a three-way agreement, on **at least three queries** of different shapes (a seek, a scan, and one that spills to a worktable):

1. QueryWatt's reported logical reads and CPU
2. SSMS running the same query with `SET STATISTICS IO, TIME ON`, same `SET` options, same parameters, same warm state
3. An independently timed execution for duration

Agreement criteria:
- **Logical reads: exact match.** Not close — exact. A mismatch means the aggregation rules in §3 are implemented wrong.
- CPU: within one resolution step.
- Duration: within the run-to-run spread of the sample.

Record the three-way comparison in `docs/validation.md` with the actual numbers. It is the most persuasive page in the repository, and it is the reason anyone should trust a number this tool prints.

---

## 14. Explicitly undecided in v1

Written down so they are not quietly decided by accident:

- Cold-cache measurement. v1 is **warm-cache only**, because `DBCC DROPCLEANBUFFERS` is unavailable on Azure SQL. Stated in the README.
- Multiple parameter sets per query (parameter-sensitive plans). One fixed set per query in v1.
- Concurrency and lock-contention effects. Not measured.
- Memory grant and spill detection. Captured in the plan XML, not yet reported.
- Azure SQL and PostgreSQL providers. The baseline format leaves room; nothing is implemented.
