using System.Data;
using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Testing;
using Xunit;

namespace QueryWatt.IntegrationTests;

/// <summary>
/// The guard as a developer meets it: an ordinary test that fails when a query gets worse, with no
/// pull request, no pipeline and no dashboard involved.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class GuardedTestTests(SqlServerFixture fixture)
{
    private const int Bucket = 11;

    private const string SeekingQuery =
        "SELECT Payload FROM dbo.GuardProbe WHERE Bucket = @Bucket;";

    // The same rows, fetched so the index cannot be used.
    private const string ScanningQuery =
        "SELECT Payload FROM dbo.GuardProbe WHERE Bucket + 0 = @Bucket;";

    [Fact]
    public void AGuardedTestPassesWhenNothingChanged()
    {
        var baselinePath = TempFile();

        try
        {
            Accept(baselinePath, SeekingQuery);

            using var guard = QueryWattGuard.Start();
            guard.Measure("guarded.probe.by-bucket", () => Run(SeekingQuery));

            // No exception is the whole point: the developer's build simply stays green.
            guard.AssertNoRegression(baselinePath);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void AGuardedTestFailsWithAReadableReportWhenAQueryGetsWorse()
    {
        var baselinePath = TempFile();

        try
        {
            Accept(baselinePath, SeekingQuery);

            using var guard = QueryWattGuard.Start();
            guard.Measure("guarded.probe.by-bucket", () => Run(ScanningQuery));

            var failure = Assert.Throws<QueryWattRegressionException>(
                () => guard.AssertNoRegression(baselinePath));

            Assert.Contains("guarded.probe.by-bucket", failure.Message, StringComparison.Ordinal);
            Assert.Contains("Reads per row rose", failure.Message, StringComparison.Ordinal);

            // The report has to say how to move on, or the developer's only options are to guess or
            // to delete the test.
            Assert.Contains("QUERYWATT_ACCEPT=1", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void AScenarioNobodyHasAcceptedYetDoesNotFailTheBuild()
    {
        var baselinePath = TempFile();
        var logged = new List<string>();

        try
        {
            using var guard = QueryWattGuard.Start(new QueryWattGuardOptions
            {
                BaselinePath = baselinePath,
                Log = logged.Add
            });

            guard.Measure("guarded.probe.brand-new", () => Run(SeekingQuery));

            guard.AssertNoRegression();

            Assert.Contains(logged, line => line.Contains("NewScenario", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void AcceptingWritesTheBaselineAndTheNextRunPasses()
    {
        var baselinePath = TempFile();

        try
        {
            using (var accepting = QueryWattGuard.Start(new QueryWattGuardOptions
            {
                BaselinePath = baselinePath,
                Accept = true
            }))
            {
                accepting.Measure("guarded.probe.accepted", () => Run(SeekingQuery));
                accepting.AssertNoRegression();
            }

            var document = InAppBaselineStore.Read(baselinePath);

            Assert.NotNull(document);
            Assert.Equal(InAppBaselineContract.SchemaVersion, document!.SchemaVersion);

            var entry = Assert.Single(document.Entries);

            Assert.Equal("guarded.probe.accepted", entry.QueryId);
            Assert.True(entry.Metrics.LogicalReads > 0);

            using var checking = QueryWattGuard.Start(new QueryWattGuardOptions
            {
                BaselinePath = baselinePath
            });

            checking.Measure("guarded.probe.accepted", () => Run(SeekingQuery));
            checking.AssertNoRegression();
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void ATestThatMeasuredNothingUsableSaysWhyRatherThanPassingQuietly()
    {
        var baselinePath = TempFile();

        try
        {
            using var guard = QueryWattGuard.Start(new QueryWattGuardOptions
            {
                BaselinePath = baselinePath,
                Level = InstrumentationLevel.Light
            });

            guard.Measure("guarded.probe.light", () => Run(SeekingQuery));

            var failure = Assert.Throws<QueryWattRegressionException>(() => guard.AssertNoRegression());

            Assert.Contains("server-side reads are missing", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void TheGuardPutsInstrumentationBackWhenItIsDone()
    {
        var before = Watt.Instrumentation;

        using (var guard = QueryWattGuard.Start())
        {
            Assert.Equal(InstrumentationLevel.Full, Watt.Instrumentation);
            guard.Measure("guarded.probe.restore", () => Run(SeekingQuery));
        }

        Assert.Equal(before, Watt.Instrumentation);
    }

    [Fact]
    public void ARelativeBaselineNameResolvesBesideTheSourceRatherThanIntoTheBuildOutput()
    {
        var resolved = BaselineLocator.Resolve(InAppBaselineContract.DefaultFileName);

        Assert.True(Path.IsPathRooted(resolved));
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", resolved, StringComparison.Ordinal);
    }

    private void Accept(string baselinePath, string commandText)
    {
        using var guard = QueryWattGuard.Start(new QueryWattGuardOptions
        {
            BaselinePath = baselinePath,
            Accept = true
        });

        guard.Measure("guarded.probe.by-bucket", () => Run(commandText));
        guard.AssertNoRegression();
    }

    private int Run(string commandText)
    {
        using var connection = fixture.OpenWrappedConnection();
        using var command = connection.CreateCommand();

        command.CommandText = commandText;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@Bucket";
        parameter.DbType = DbType.Int32;
        parameter.Value = Bucket;
        command.Parameters.Add(parameter);

        using var reader = command.ExecuteReader();

        var rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        return rows;
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
}
