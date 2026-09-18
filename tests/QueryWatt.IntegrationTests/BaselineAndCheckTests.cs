using System.Data;
using System.Data.Common;
using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;
using QueryWatt.SqlServer.Fingerprints;
using Xunit;

namespace QueryWatt.IntegrationTests;

/// <summary>
/// The guard, end to end: measure, accept, measure again, and see the verdict a developer would see.
/// </summary>
/// <remarks>
/// These tests use their own table, seeded large enough that an index seek and a table scan cost
/// visibly different amounts. On the fifty-row tables the other suites use, both plans fit in a
/// couple of pages and a real regression would be indistinguishable from noise — which is itself the
/// reason QueryWatt refuses to judge queries below a floor of logical reads.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class BaselineAndCheckTests(SqlServerFixture fixture) : IDisposable
{
    private const int Bucket = 7;

    private const string SeekingQuery = """
        SELECT Payload FROM dbo.GuardProbe WHERE Bucket = @Bucket;
        """;

    // The same rows, fetched so the index cannot be used — what happens the day someone wraps a
    // column in a function to "simplify" a filter.
    private const string ScanningQuery = """
        SELECT Payload FROM dbo.GuardProbe WHERE Bucket + 0 = @Bucket;
        """;

    public void Dispose() => Watt.Instrumentation = InstrumentationLevel.Off;

    [Fact]
    public void AcceptThenCheckOnUnchangedCodeAndDataPasses()
    {
        EnsureGuardTable();

        var baselinePath = TempFile(".json");

        try
        {
            InAppBaselineStore.Write(
                baselinePath,
                InAppBaselineFactory.CreateDocument(Measure("guard.by-bucket", SeekingQuery, runs: 3), "test"));

            var result = VerdictMatrix.Compare(
                InAppBaselineStore.Read(baselinePath),
                Measure("guard.by-bucket", SeekingQuery, runs: 3));

            var verdict = Assert.Single(result.Results);

            Assert.Equal(InAppVerdict.Unchanged, verdict.Verdict);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void AQueryThatStopsUsingItsIndexFailsTheCheck()
    {
        EnsureGuardTable();

        var baseline = InAppBaselineFactory.CreateDocument(
            Measure("guard.by-bucket", SeekingQuery, runs: 3),
            "test");

        var result = VerdictMatrix.Compare(
            baseline,
            Measure("guard.by-bucket", ScanningQuery, runs: 3));

        var verdict = Assert.Single(result.Results);

        Assert.True(
            verdict.Verdict is InAppVerdict.Regression or InAppVerdict.PlanRegression,
            $"Expected a regression, got {verdict.Verdict}: {verdict.Reason}");
        Assert.Equal(1, result.ExitCode);
        Assert.True(
            verdict.Current!.ReadsPerRow > verdict.Baseline!.ReadsPerRow,
            $"Expected reads per row to rise: {verdict.Baseline.ReadsPerRow} → {verdict.Current.ReadsPerRow}");

        // Same rows, much more work: the row count is what keeps this honest.
        Assert.Equal(verdict.Baseline.RowsReturned, verdict.Current.RowsReturned);
    }

    [Fact]
    public void TheSameQueryCalledWithAnExtraFilterIsANewScenarioRatherThanAFailure()
    {
        EnsureGuardTable();

        var baseline = InAppBaselineFactory.CreateDocument(
            Measure("guard.by-bucket", SeekingQuery, runs: 1),
            "test");

        var narrower = Measure(
            "guard.by-bucket",
            "SELECT Payload FROM dbo.GuardProbe WHERE Bucket = @Bucket AND Payload LIKE @Payload;",
            runs: 1,
            extraPayloadFilter: true);

        var result = VerdictMatrix.Compare(baseline, narrower);
        var verdict = Assert.Single(result.Results);

        Assert.Equal(InAppVerdict.NewScenario, verdict.Verdict);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void TheServerFingerprintIsStableAndNamesWhatDiffers()
    {
        using var connection = fixture.OpenConnection();

        var first = ServerFingerprintProbe.Read(connection);
        var second = ServerFingerprintProbe.Read(connection);

        Assert.Equal(first.Value, second.Value);
        Assert.NotEmpty(first.ProductVersion);
        Assert.Empty(first.Differences(second));

        var elsewhere = first with { CompatibilityLevel = first.CompatibilityLevel - 10 };

        Assert.NotEqual(first.Value, elsewhere.Value);
        Assert.Contains(
            first.Differences(elsewhere),
            difference => difference.StartsWith("compatibility level", StringComparison.Ordinal));
    }

    [Fact]
    public void AMeasuredRunAcceptedThroughTheFileKeepsItsNumbers()
    {
        var runPath = TempFile(".jsonl");
        var baselinePath = TempFile(".json");
        var previousSink = Watt.Sink;

        try
        {
            using (var sink = new JsonLinesMeasurementSink(runPath))
            {
                Watt.Sink = sink;
                Watt.Instrumentation = InstrumentationLevel.Full;

                using var scope = Watt.Measure("orders.count.guard");
                using var connection = fixture.OpenConnection();
                using var command = connection.CreateCommand();

                command.CommandText = "SELECT COUNT(*) FROM dbo.[Order];";
                command.ExecuteScalar();

                scope.Complete();
            }

            var samples = InAppBaselineFactory.Summarise(MeasurementRecordFile.Read(runPath));
            InAppBaselineStore.Write(baselinePath, InAppBaselineFactory.CreateDocument(samples, "test"));

            var entry = Assert.Single(InAppBaselineStore.Read(baselinePath)!.Entries);
            var sample = Assert.Single(samples);

            Assert.Equal("orders.count.guard", entry.QueryId);
            Assert.Equal(sample.Metrics.LogicalReads, entry.Metrics.LogicalReads);
            Assert.True(entry.Metrics.LogicalReads > 0);
            Assert.Equal(sample.Metrics.ReadsPerRow, entry.Metrics.ReadsPerRow);
        }
        finally
        {
            Watt.Sink = previousSink;
            Watt.Instrumentation = InstrumentationLevel.Off;
            File.Delete(runPath);
            File.Delete(baselinePath);
        }
    }

    private IReadOnlyList<InAppSample> Measure(
        string queryId,
        string commandText,
        int runs,
        bool extraPayloadFilter = false)
    {
        var sink = new InMemoryMeasurementSink();
        var previousSink = Watt.Sink;

        try
        {
            Watt.Sink = sink;
            Watt.Instrumentation = InstrumentationLevel.Full;

            for (var run = 0; run < runs; run++)
            {
                using var scope = Watt.Measure(queryId);
                using var connection = fixture.OpenWrappedConnection();
                using var command = connection.CreateCommand();

                command.CommandText = commandText;
                Add(command, "@Bucket", DbType.Int32, Bucket);

                if (extraPayloadFilter)
                {
                    Add(command, "@Payload", DbType.String, "x%");
                }

                // The reader must close before the scope does: the server sends a statement's
                // statistics after its last result set, so completing a scope with a reader still
                // open measures nothing.
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                    }
                }

                scope.Complete();
            }

            var samples = InAppBaselineFactory.Summarise(sink.RootRecords);

            Assert.True(
                samples.Count > 0,
                "No baseline-eligible records were produced. " + Describe(sink.RootRecords));

            return samples;
        }
        finally
        {
            Watt.Sink = previousSink;
            Watt.Instrumentation = InstrumentationLevel.Off;
        }
    }

    private void EnsureGuardTable()
    {
        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandTimeout = 120;
        command.CommandText = """
            IF OBJECT_ID('dbo.GuardProbe', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.GuardProbe
                (
                    GuardProbeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GuardProbe PRIMARY KEY,
                    Bucket       int          NOT NULL,
                    Payload      nvarchar(200) NOT NULL
                );

                WITH numbers AS
                (
                    SELECT TOP (20000)
                           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                    FROM   sys.all_objects a
                    CROSS JOIN sys.all_objects b
                )
                INSERT INTO dbo.GuardProbe (Bucket, Payload)
                SELECT n % 200, REPLICATE(N'x', 100)
                FROM   numbers;

                CREATE NONCLUSTERED INDEX IX_GuardProbe_Bucket
                    ON dbo.GuardProbe (Bucket) INCLUDE (Payload);
            END
            """;

        command.ExecuteNonQuery();
    }

    private static string Describe(IReadOnlyList<MeasurementRecord> records) =>
        records.Count == 0
            ? "The sink received nothing at all."
            : string.Join(" || ", records.Select(record =>
                $"{record.QueryId} status={record.Status} commands={record.OwnCommandCount}"
                + $" metricsComplete={record.MetricsComplete}"
                + $" reads=[{string.Join(",", record.Commands.Select(c => c.LogicalReads?.ToString() ?? "null"))}]"
                + $" cpu=[{string.Join(",", record.Commands.Select(c => c.CpuTimeMilliseconds?.ToString() ?? "null"))}]"
                + $" duration=[{string.Join(",", record.Commands.Select(c => c.ClientDurationMilliseconds?.ToString("0.##") ?? "null"))}]"
                + $" text=[{string.Join(" ; ", record.Commands.Select(c => c.CommandText.Length > 40 ? c.CommandText[..40] : c.CommandText))}]"
                + $" diag=[{string.Join("/", record.Diagnostics.Select(d => d.Code))}]"));

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string TempFile(string extension) =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
}
