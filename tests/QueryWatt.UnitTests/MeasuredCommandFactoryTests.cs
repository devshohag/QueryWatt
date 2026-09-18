using System.Collections;
using System.Data;
using QueryWatt.Core;
using QueryWatt.Core.Instrumentation;
using QueryWatt.SqlServer;
using QueryWatt.SqlServer.Capture;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class MeasuredCommandFactoryTests
{
    [Fact]
    public void WithoutServerStatistics_ReadsAndCpuStayNull()
    {
        var command = MeasuredCommandFactory.Create(
            1,
            "SELECT 1",
            CommandType.Text,
            MeasurementSource.AdoNet,
            [],
            clientDurationMilliseconds: 11.5,
            exception: null,
            serverStatistics: null,
            clientStatistics: ClientCommandStatistics.None);

        Assert.Null(command.LogicalReads);
        Assert.Null(command.CpuTimeMilliseconds);
        Assert.Null(command.RowsReturned);
        Assert.Equal(11.5, command.ClientDurationMilliseconds);
        Assert.Equal(CommandOutcome.Succeeded, command.Outcome);
        Assert.False(command.HasGatingMetrics);
    }

    [Fact]
    public void WithServerStatistics_TheGatingMetricsArePresent()
    {
        var parsed = new ParsedStatistics(
        [
            new StatementMetrics(
                1,
                4,
                9,
                [new TableIoMetrics("Order", 1, 1, 84, 2, 3, 0, 0, 0)])
        ]);

        var command = MeasuredCommandFactory.Create(
            1,
            "SELECT 1",
            CommandType.Text,
            MeasurementSource.AdoNet,
            [],
            clientDurationMilliseconds: 11.5,
            exception: null,
            serverStatistics: parsed,
            clientStatistics: new ClientCommandStatistics(37, 1, 4096));

        Assert.Equal(84L, command.LogicalReads);
        Assert.Equal(4L, command.CpuTimeMilliseconds);
        Assert.Equal(9L, command.ServerElapsedTimeMilliseconds);
        Assert.Equal(37L, command.RowsReturned);
        Assert.Equal(1L, command.ServerRoundtrips);
        Assert.True(command.HasGatingMetrics);
    }

    [Fact]
    public void AFailedCommand_KeepsItsMetricsAndItsError()
    {
        var command = MeasuredCommandFactory.Create(
            2,
            "SELECT 1",
            CommandType.Text,
            MeasurementSource.AdoNet,
            [],
            clientDurationMilliseconds: 30_000,
            exception: new TimeoutException("Execution Timeout Expired."),
            serverStatistics: null,
            clientStatistics: ClientCommandStatistics.None);

        Assert.Equal(CommandOutcome.Failed, command.Outcome);
        Assert.Equal(typeof(TimeoutException).FullName, command.ExceptionType);
        Assert.Equal("Execution Timeout Expired.", command.ExceptionMessage);
    }

    [Fact]
    public void LongCommandText_IsTruncated()
    {
        var command = MeasuredCommandFactory.Create(
            1,
            new string('x', 100),
            CommandType.Text,
            MeasurementSource.AdoNet,
            [],
            clientDurationMilliseconds: 1,
            exception: null,
            serverStatistics: null,
            clientStatistics: ClientCommandStatistics.None,
            maxCommandTextLength: 20);

        Assert.Equal(20, command.CommandText.Length);
    }

    [Fact]
    public void ClientStatistics_KeepAbsentCountersNull()
    {
        var statistics = new Hashtable
        {
            { "SelectRows", 37L },
            { "ServerRoundtrips", 2 }
        };

        var read = ClientCommandStatistics.From(statistics);

        Assert.Equal(37L, read.RowsReturned);
        Assert.Equal(2L, read.ServerRoundtrips);
        Assert.Null(read.BytesReceived);
    }

    [Fact]
    public void ClientStatistics_FromNothing_IsAllNull()
    {
        var read = ClientCommandStatistics.From(null);

        Assert.Null(read.RowsReturned);
        Assert.Null(read.ServerRoundtrips);
        Assert.Null(read.BytesReceived);
    }
}
