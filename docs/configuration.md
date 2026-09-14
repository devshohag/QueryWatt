# Configuration — schema version 1

Week 2 introduces a strict YAML loader. Unknown property names are rejected so
a typo cannot silently change a measurement.

```yaml
schemaVersion: 1

connection:
  environmentVariable: QUERYWATT_CONNECTION_STRING

measurement:
  warmupRuns: 3
  measuredRuns: 20
  commandTimeoutSeconds: 60

queries:
  - name: customer-seek
    file: TicketingDatabase/queries/seek-parameterized.sql
    commandType: text
    parameters:
      - name: CustomerId
        type: int32
        value: "417"
```

Query file paths are relative to the YAML file, not the process working
directory. Query names and parameter names are unique ignoring case. Queries
run sequentially in the order listed.

## Parameter types

Supported names are:

- `string`, `ansiString`
- `int16`, `int32`, `int64`
- `decimal`, `single`, `double`
- `boolean`
- `guid`
- `date`, `dateTime`, `dateTime2`, `dateTimeOffset`, `time`
- `binary` (Base64 text)

Parameter values should be quoted YAML scalars. They are parsed with invariant
culture into the declared `DbType`; QueryWatt never calls `AddWithValue`.
Optional `size`, `precision`, and `scale` fields are copied to `SqlParameter`.
A YAML `null` value becomes database `NULL`.

## Statistics

Each metric retains its ordered raw values and reports minimum, raw median,
IQR-filtered median, maximum, mean, population standard deviation, Q1, Q3, and
the 1.5-IQR fences. Outliers remain in the raw array and their one-based run
numbers are listed.

Quartiles and percentiles use R-7 linear interpolation. p95 is `null` for
20–49 runs and is produced only for 50 or more measured runs. Future regression
decisions will use the filtered median; Week 2 does not make a pass/fail verdict.
