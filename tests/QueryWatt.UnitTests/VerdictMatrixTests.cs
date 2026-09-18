using QueryWatt.Baselines.InApp;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class VerdictMatrixTests
{
    [Fact]
    public void AQueryThatCostsTheSamePerRowPasses()
    {
        var verdict = Compare(
            baseline: Metrics(reads: 1_000, rows: 100),
            current: Metrics(reads: 1_020, rows: 100));

        Assert.Equal(InAppVerdict.Unchanged, verdict.Verdict);
        Assert.False(verdict.Fails);
    }

    [Fact]
    public void AQueryThatReadsMuchMorePerRowIsARegression()
    {
        var verdict = Compare(
            baseline: Metrics(reads: 1_000, rows: 100),
            current: Metrics(reads: 4_000, rows: 100));

        Assert.Equal(InAppVerdict.Regression, verdict.Verdict);
        Assert.True(verdict.Fails);
        Assert.Contains("Reads per row rose", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DataGrowingIsNotARegression()
    {
        // Ten times the rows, ten times the reads: the query is exactly as efficient as it was.
        // This is the case a raw-reads comparison gets wrong, and the reason it would have been
        // abandoned by its third false alarm.
        var verdict = Compare(
            baseline: Metrics(reads: 1_000, rows: 100),
            current: Metrics(reads: 10_000, rows: 1_000));

        Assert.Equal(InAppVerdict.DataChange, verdict.Verdict);
        Assert.False(verdict.Fails);
    }

    [Fact]
    public void AQueryCalledWithDifferentFiltersIsANewScenarioRatherThanAFailure()
    {
        var baseline = Document(Entry("orders.search", "p[CustomerId:Guid]", Metrics(500, 50)));

        var result = VerdictMatrix.Compare(
            baseline,
            [Sample("orders.search", "p[CustomerId:Guid, PlacedAfter:DateTime2, StatusId:Int32]", Metrics(9_000, 50))]);

        var verdict = Assert.Single(result.Results);

        Assert.Equal(InAppVerdict.NewScenario, verdict.Verdict);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void TheSameQueryThroughADifferentStackIsNeverComparedWithTheOldOne()
    {
        var baseline = Document(Entry("orders.by-customer", "p[Id:Guid]", Metrics(500, 50), source: "Dapper"));

        var result = VerdictMatrix.Compare(
            baseline,
            [Sample("orders.by-customer", "p[Id:Guid]", Metrics(9_000, 50), source: "EfCore")]);

        Assert.Equal(InAppVerdict.NewScenario, Assert.Single(result.Results).Verdict);
    }

    [Fact]
    public void APlanChangeWithMoreReadsNamesThePlan()
    {
        var entry = Entry("orders.by-customer", "p[Id:Guid]", Metrics(1_000, 100)) with
        {
            PlanFingerprint = "plan-a"
        };

        var sample = Sample("orders.by-customer", "p[Id:Guid]", Metrics(4_000, 100)) with
        {
            PlanFingerprint = "plan-b"
        };

        var verdict = VerdictMatrix.Compare(entry, sample, InAppThresholds.Default);

        Assert.Equal(InAppVerdict.PlanRegression, verdict.Verdict);
        Assert.True(verdict.Fails);
        Assert.Contains("the plan changed", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanChangeThatCostsNothingYetIsOnlySuspect()
    {
        var entry = Entry("orders.by-customer", "p[Id:Guid]", Metrics(1_000, 100)) with
        {
            PlanFingerprint = "plan-a"
        };

        var sample = Sample("orders.by-customer", "p[Id:Guid]", Metrics(1_010, 100)) with
        {
            PlanFingerprint = "plan-b"
        };

        var verdict = VerdictMatrix.Compare(entry, sample, InAppThresholds.Default);

        Assert.Equal(InAppVerdict.Suspect, verdict.Verdict);
        Assert.False(verdict.Fails);
    }

    [Fact]
    public void ADifferentServerMakesTheComparisonIncomparableRatherThanFailing()
    {
        var entry = Entry("orders.by-customer", "p[Id:Guid]", Metrics(1_000, 100)) with
        {
            EnvironmentFingerprint = "server-a"
        };

        var sample = Sample("orders.by-customer", "p[Id:Guid]", Metrics(9_000, 100)) with
        {
            EnvironmentFingerprint = "server-b"
        };

        var verdict = VerdictMatrix.Compare(entry, sample, InAppThresholds.Default);

        Assert.Equal(InAppVerdict.Incomparable, verdict.Verdict);
        Assert.False(verdict.Fails);
        Assert.Contains("server differs", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATinyQueryGrowingByOneReadIsNotWorthReporting()
    {
        var verdict = Compare(
            baseline: Metrics(reads: 2, rows: 1),
            current: Metrics(reads: 3, rows: 1));

        Assert.Equal(InAppVerdict.Unchanged, verdict.Verdict);
    }

    [Fact]
    public void AQueryThatGotCheaperPerRowIsReportedAsImproved()
    {
        var verdict = Compare(
            baseline: Metrics(reads: 4_000, rows: 100),
            current: Metrics(reads: 1_000, rows: 100));

        Assert.Equal(InAppVerdict.Improved, verdict.Verdict);
        Assert.False(verdict.Fails);
    }

    [Fact]
    public void TheRunTakesTheWorstVerdictAndTheExitCodeThatGoesWithIt()
    {
        var baseline = Document(
            Entry("a.fine", "p[]", Metrics(1_000, 100)),
            Entry("b.regressed", "p[]", Metrics(1_000, 100)));

        var result = VerdictMatrix.Compare(
            baseline,
            [
                Sample("a.fine", "p[]", Metrics(1_000, 100)),
                Sample("b.regressed", "p[]", Metrics(5_000, 100))
            ]);

        Assert.Equal(InAppVerdict.Regression, result.Verdict);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("b.regressed", Assert.Single(result.Failures).QueryId);
    }

    [Fact]
    public void AcceptingAgainReplacesOnlyTheScenariosThatWereMeasured()
    {
        var existing = Document(
            Entry("kept.elsewhere", "p[]", Metrics(100, 10)),
            Entry("replaced.here", "p[]", Metrics(100, 10)));

        var merged = InAppBaselineFactory.Merge(
            existing,
            [Sample("replaced.here", "p[]", Metrics(200, 10))],
            "1.2.3");

        Assert.Equal(2, merged.Entries.Count);
        Assert.Equal(100, merged.Find("kept.elsewhere", "p[]", "AdoNet")!.Metrics.LogicalReads);
        Assert.Equal(200, merged.Find("replaced.here", "p[]", "AdoNet")!.Metrics.LogicalReads);
    }

    [Fact]
    public void ABaselineSurvivesTheRoundTripThroughItsFile()
    {
        var document = Document(Entry("orders.by-customer", "p[Id:Guid]", Metrics(1_234, 56)));
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        try
        {
            InAppBaselineStore.Write(path, document);

            var read = InAppBaselineStore.Read(path);

            Assert.NotNull(read);
            Assert.Equal(InAppBaselineContract.SchemaVersion, read!.SchemaVersion);

            var entry = Assert.Single(read.Entries);

            Assert.Equal("orders.by-customer", entry.QueryId);
            Assert.Equal(1_234, entry.Metrics.LogicalReads);
            Assert.Equal(document.Entries[0].Metrics.ReadsPerRow, entry.Metrics.ReadsPerRow);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingBaselineFileIsNotAnError()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        Assert.Null(InAppBaselineStore.Read(path));
    }

    private static InAppVerdictResult Compare(InAppMetrics baseline, InAppMetrics current) =>
        VerdictMatrix.Compare(
            Entry("orders.by-customer", "p[Id:Guid]", baseline),
            Sample("orders.by-customer", "p[Id:Guid]", current),
            InAppThresholds.Default);

    private static InAppMetrics Metrics(long reads, long rows) =>
        new(reads, 10L, rows, 12.5d, InAppBaselineFactory.ReadsPerRow(reads, rows));

    private static InAppBaselineEntry Entry(
        string queryId,
        string scenarioKey,
        InAppMetrics metrics,
        string source = "AdoNet") =>
        new(queryId, scenarioKey, source, DateTimeOffset.UnixEpoch, 5, metrics);

    private static InAppSample Sample(
        string queryId,
        string scenarioKey,
        InAppMetrics metrics,
        string source = "AdoNet") =>
        new(queryId, scenarioKey, source, 5, metrics);

    private static InAppBaselineDocument Document(params InAppBaselineEntry[] entries) =>
        new(InAppBaselineContract.SchemaVersion, "1.0.0", DateTimeOffset.UnixEpoch, entries);
}
