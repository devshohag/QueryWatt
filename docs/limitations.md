# QueryWatt v1 Limitations

- Measurements are warm-cache only.
- Queries run sequentially; concurrency and lock contention are not measured.
- One fixed parameter set is supported per registered query.
- Logical reads are buffer-pool page accesses, not storage-energy measurements.
- CPU time has whole-millisecond resolution and is unreliable below the
  contract's 10 ms floor.
- Client duration includes transferring and draining every result set and is
  sensitive to runner load.
- The v1 plan hash will come from a separate estimated-plan capture. It is not
  proof of the actual plan used by every measured run. Adaptive joins, memory
  grant feedback, and parameter-sensitive plan variants may differ.
- Hardware energy is not measured. A future Wh value will be an estimate and
  will require a user-supplied coefficient.
- QueryWatt v1 never estimates carbon or CO2 emissions.
