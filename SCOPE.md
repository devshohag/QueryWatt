# QueryWatt v1 Scope

QueryWatt is a SQL Server efficiency regression guard. It proves whether a
repeatable query/code change caused registered queries to consume more
resources than an approved baseline. A schema or seed change currently causes
an explicit environment-mismatch refusal instead of a fabricated comparison.

## In scope

- `querywatt init`
- `querywatt baseline`
- `querywatt verify`
- Warm-cache, sequential execution against deterministic synthetic data
- Logical reads as the default CI gate
- CPU and client duration as supporting metrics
- Git-tracked, schema-versioned baselines
- Console, JSON, and Markdown output
- Optional, transparently labelled CPU-based energy estimates

## Explicitly out of scope

- Query rewriting or optimization rules
- Index recommendations
- EF Core analysis
- AI or MCP integration
- GUI or dashboard
- Carbon or CO2 estimates
- Production monitoring or production database modification
- Cold-cache, concurrency, and lock-contention benchmarks
- Multiple parameter sets per query
- Automatic schema-change comparison
- PostgreSQL and Azure SQL providers
