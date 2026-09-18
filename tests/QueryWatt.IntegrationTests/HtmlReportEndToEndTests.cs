using System.Data;
using QueryWatt.Baselines;
using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using Xunit;

namespace QueryWatt.IntegrationTests;

/// <summary>
/// The whole loop a team without pull requests actually runs: measure, accept, measure again, and
/// get a file they can open.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class HtmlReportEndToEndTests(SqlServerFixture fixture) : IDisposable
{
    private const int Bucket = 3;

    public void Dispose() => Watt.Instrumentation = InstrumentationLevel.Off;

    [Fact]
    public void ARunWithABaselineProducesAPageThatNamesTheRegression()
    {
        var baselinePath = TempFile(".json");
        var reportPath = TempFile(".html");

        try
        {
            var accepted = InAppBaselineFactory.CreateDocument(
                InAppBaselineFactory.Summarise(Run(
                    "report.probe.by-bucket",
                    "SELECT Payload FROM dbo.GuardProbe WHERE Bucket = @Bucket;")),
                BaselineContract.ToolVersion);

            InAppBaselineStore.Write(baselinePath, accepted);

            var records = Run(
                "report.probe.by-bucket",
                "SELECT Payload FROM dbo.GuardProbe WHERE Bucket + 0 = @Bucket;");

            var model = HtmlReportModelFactory.Create(
                ObservationReportFactory.Create(records),
                VerdictMatrix.Compare(
                    InAppBaselineStore.Read(baselinePath),
                    InAppBaselineFactory.Summarise(records)),
                BaselineContract.ToolVersion);

            File.WriteAllText(reportPath, HtmlReportRenderer.Render(model));

            var html = File.ReadAllText(reportPath);

            Assert.True(model.FailureCount > 0, "The scan should have regressed against the seek.");
            Assert.Contains("report.probe.by-bucket", html, StringComparison.Ordinal);
            Assert.Contains("Regression", html, StringComparison.Ordinal);
            Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);

            // A page nobody can open is not a report: it has to be a whole document on its own.
            Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
            Assert.EndsWith("</html>", html.TrimEnd(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(baselinePath);
            File.Delete(reportPath);
        }
    }

    [Fact]
    public void ARunWithNoBaselineStillProducesAReadablePage()
    {
        var records = Run(
            "report.probe.first-look",
            "SELECT Payload FROM dbo.GuardProbe WHERE Bucket = @Bucket;");

        var model = HtmlReportModelFactory.Create(
            ObservationReportFactory.Create(records),
            verification: null,
            BaselineContract.ToolVersion);

        Assert.False(model.HasBaseline);
        Assert.True(model.TotalLogicalReads > 0);

        var html = HtmlReportRenderer.Render(model);

        Assert.Contains("report.probe.first-look", html, StringComparison.Ordinal);
    }

    private IReadOnlyList<MeasurementRecord> Run(string queryId, string commandText)
    {
        var sink = new InMemoryMeasurementSink();
        var previousSink = Watt.Sink;

        try
        {
            Watt.Sink = sink;
            Watt.Instrumentation = InstrumentationLevel.Full;

            using (var scope = Watt.Measure(queryId))
            {
                using (var connection = fixture.OpenWrappedConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = commandText;

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@Bucket";
                    parameter.DbType = DbType.Int32;
                    parameter.Value = Bucket;
                    command.Parameters.Add(parameter);

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                    }
                }

                scope.Complete();
            }

            return sink.RootRecords;
        }
        finally
        {
            Watt.Sink = previousSink;
            Watt.Instrumentation = InstrumentationLevel.Off;
        }
    }

    private static string TempFile(string extension) =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
}
