# Energy model — `cpu-coefficient-v0.1`

QueryWatt does not measure hardware energy and does not calculate carbon. The
optional Week 4 model turns measured SQL Server CPU time into a transparent,
user-configured estimate.

## Formula

SQL Server `STATISTICS TIME` CPU milliseconds represent accumulated busy CPU
time. QueryWatt first reports the measured resource index:

```text
cpuCoreSecondsPerExecution = filteredMedianCpuMilliseconds / 1000
cpuCoreSecondsPerDay = cpuCoreSecondsPerExecution × executionsPerDay
```

When the user explicitly enables energy and supplies a coefficient:

```text
wattHours = cpuCoreSeconds × wattsPerBusyCore / 3600
```

`wattsPerBusyCore` is watts per busy core, a power rate. It is not watts per
core-second. QueryWatt ships no default coefficient because server power,
utilization, processor generation, cooling boundary, and virtualization make a
universal value indefensible.

## Configuration

```yaml
energy:
  enabled: true
  wattsPerBusyCore: 0.9 # user-supplied; example only, not a QueryWatt default

queries:
  - name: example
    executionsPerDay: 18420
```

Without `executionsPerDay`, QueryWatt can show per-execution resource and energy
values but not daily values. With energy disabled or the coefficient absent,
only CPU-core-seconds are reported.

## Limitations and forbidden claims

- The estimate is not a power-meter measurement.
- It is not whole-server or whole-database energy.
- It excludes idle power, memory, network, cooling, and embodied hardware.
- Logical reads are predominantly buffer-pool hits and are not used as storage
  energy. A future storage term would require physical I/O evidence.
- QueryWatt v1 never outputs gCO2e. Carbon requires grid intensity, embodied
  emissions, and an explicit functional unit.
- The estimate can compare scenarios only when the same coefficient, workload
  rate, environment fingerprint, and measurement protocol are used.

Reports place measured resource values and estimated energy in separate blocks.
Every estimate includes the model id, coefficient, and the label
`estimated-not-measured-not-carbon`.
