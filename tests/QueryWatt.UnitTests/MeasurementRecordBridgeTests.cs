using QueryWatt.Core;
using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.UnitTests;

/// <summary>
/// One scope execution is one run, so a completed record must convert into exactly one
/// <see cref="RunMetrics"/> and then flow through the v1 statistics and baseline pipeline.
/// </summary>
public sealed class MeasurementRecordBridgeTests
{
    [Fact]
    public void ACompletedRecord_BecomesOneRunMetrics()
    {
        var record = Record(
            MeasurementStatus.Completed,
            TestCommands.Measured(ordinal: 1, logicalReads: 84, cpuMilliseconds: 4, rows: 37),
            TestCommands.Measured(ordinal: 2, logicalReads: 116, cpuMilliseconds: 6, rows: 5));

        var converted = record.TryCreateRunMetrics(1, out var runMetrics);

        Assert.True(converted);
        Assert.NotNull(runMetrics);
        Assert.Equal(1, runMetrics!.RunNumber);
        Assert.Equal(200L, runMetrics.LogicalReads);
        Assert.Equal(10L, runMetrics.CpuTimeMilliseconds);
        Assert.Equal(42L, runMetrics.RowsReturned);
        Assert.Equal(2, runMetrics.Statements.Count);
        Assert.Equal(19d, runMetrics.ClientDurationMilliseconds);
    }

    [Fact]
    public void ARecordMissingGatingMetrics_ProducesNothing()
    {
        var record = Record(MeasurementStatus.Completed, TestCommands.WithoutGatingMetrics());

        Assert.False(record.TryCreateRunMetrics(1, out var runMetrics));
        Assert.Null(runMetrics);
        Assert.False(record.MetricsComplete);
    }

    [Fact]
    public void AnAbandonedRecord_NeverBecomesRunMetrics()
    {
        var record = Record(MeasurementStatus.Abandoned, TestCommands.Measured());

        Assert.False(record.TryCreateRunMetrics(1, out _));
        Assert.False(record.IsBaselineEligible);
    }

    [Fact]
    public void ARecordWithNoCommands_ProducesNothing()
    {
        var record = Record(MeasurementStatus.Completed);

        Assert.False(record.TryCreateRunMetrics(1, out _));
    }

    [Fact]
    public void ACallerSuppliedRowCount_WinsOverTheObservedOne()
    {
        var record = Record(MeasurementStatus.Completed, TestCommands.Measured(rows: 0)) with
        {
            CallerReportedRows = 412
        };

        Assert.True(record.TryCreateRunMetrics(1, out var runMetrics));
        Assert.Equal(412L, runMetrics!.RowsReturned);
    }

    [Fact]
    public void AnInvalidRunNumber_IsRejected()
    {
        var record = Record(MeasurementStatus.Completed, TestCommands.Measured());

        Assert.False(record.TryCreateRunMetrics(0, out _));
    }

    private static MeasurementRecord Record(
        MeasurementStatus status,
        params MeasuredCommand[] commands) =>
        new(
            "orders.pending-by-customer",
            "p[CustomerId:Guid]",
            MeasurementSource.AdoNet,
            status,
            0,
            null,
            DateTimeOffset.UnixEpoch,
            19d,
            commands,
            [],
            []);
}
