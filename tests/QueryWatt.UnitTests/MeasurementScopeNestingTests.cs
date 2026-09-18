using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.UnitTests;

[Collection(InstrumentationCollection.Name)]
public sealed class MeasurementScopeNestingTests : IDisposable
{
    private readonly WattFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ANestedScopeBecomesAChild_NotAnError()
    {
        using (var parent = Watt.Measure("checkout.prepare"))
        {
            using (var child = Watt.Measure("carts.get-with-lines"))
            {
                child.State!.RecordCommand(TestCommands.Measured(logicalReads: 210));
                child.Complete();
            }

            parent.State!.RecordCommand(TestCommands.Measured(logicalReads: 412));
            parent.Complete();
        }

        var records = _fixture.Sink.Records;

        Assert.Equal(2, records.Count);

        var childRecord = Assert.Single(records, record => record.Depth == 1);
        var parentRecord = Assert.Single(_fixture.Sink.RootRecords);

        Assert.Equal("carts.get-with-lines", childRecord.QueryId);
        Assert.Equal("checkout.prepare", childRecord.ParentQueryId);
        Assert.Equal("checkout.prepare", parentRecord.QueryId);
        Assert.Single(parentRecord.Children);
        Assert.Equal(2, parentRecord.TotalCommandCount);
        Assert.Equal(1, parentRecord.OwnCommandCount);
    }

    [Fact]
    public void TheInnermostScopeOwnsTheAmbientContext()
    {
        using var parent = Watt.Measure("checkout.prepare");

        Assert.Equal("checkout.prepare", Watt.Current!.QueryId);

        using (var child = Watt.Measure("carts.get-with-lines"))
        {
            Assert.Equal("carts.get-with-lines", Watt.Current!.QueryId);
            child.Complete();
        }

        Assert.Equal("checkout.prepare", Watt.Current!.QueryId);
        parent.Complete();
    }

    [Fact]
    public void TheAmbientContextIsEmptyOutsideEveryScope()
    {
        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            scope.Complete();
        }

        Assert.Null(Watt.Current);
    }

    [Fact]
    public void BeyondTheDepthCap_ScopesAttachToTheDeepestAcceptedScope()
    {
        Descend(0);

        var deepest = _fixture.Sink.Records
            .OrderByDescending(record => record.Depth)
            .First();

        Assert.Equal(MeasurementContext.MaxDepth - 1, deepest.Depth);
        Assert.Contains(deepest.Diagnostics, d => d.Code == DiagnosticCode.NestingDepthExceeded);
        Assert.Equal(MeasurementStatus.Completed, deepest.Status);

        // Exactly MaxDepth scopes were accepted; the extra five attached instead of nesting.
        Assert.Equal(MeasurementContext.MaxDepth, _fixture.Sink.Records.Count);

        static void Descend(int level)
        {
            using var scope = Watt.Measure($"level-{level}");
            scope.State?.RecordCommand(TestCommands.Measured(ordinal: level + 1));

            if (level < MeasurementContext.MaxDepth + 4)
            {
                Descend(level + 1);
            }

            scope.Complete();
        }
    }
}
