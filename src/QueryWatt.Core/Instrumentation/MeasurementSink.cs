namespace QueryWatt.Core.Instrumentation;

/// <summary>Where finished scopes go. Every scope is published, at every depth.</summary>
public interface IMeasurementSink
{
    /// <summary>
    /// Must never throw: a measurement fault may not change application behaviour.
    /// Root scopes are identifiable by <see cref="MeasurementRecord.Depth"/> equal to zero.
    /// </summary>
    void Publish(MeasurementRecord record);
}

/// <summary>Discards everything. The default, so an unconfigured application pays nothing.</summary>
public sealed class NullMeasurementSink : IMeasurementSink
{
    public static readonly NullMeasurementSink Instance = new();

    private NullMeasurementSink()
    {
    }

    public void Publish(MeasurementRecord record)
    {
    }
}

/// <summary>
/// Keeps records in memory with a bounded capacity. The collector behind observe mode and the
/// test assertions; it drops the oldest record when full rather than growing without limit.
/// </summary>
public sealed class InMemoryMeasurementSink(int capacity = 10_000) : IMeasurementSink
{
    private readonly Queue<MeasurementRecord> _records = new();
    private readonly Lock _gate = new();
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

    public int DroppedCount { get; private set; }

    public IReadOnlyList<MeasurementRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    public IReadOnlyList<MeasurementRecord> RootRecords =>
        Records.Where(record => record.Depth == 0).ToArray();

    public void Publish(MeasurementRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            if (_records.Count == _capacity)
            {
                _records.Dequeue();
                DroppedCount++;
            }

            _records.Enqueue(record);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
            DroppedCount = 0;
        }
    }
}
