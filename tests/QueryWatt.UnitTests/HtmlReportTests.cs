using System.Data;
using System.Text.RegularExpressions;
using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed partial class HtmlReportTests
{
    [Fact]
    public void ThePageMakesNoNetworkRequests()
    {
        var html = Render(WithBaseline());

        // The whole point of the file is that it works from disk, on a machine that has never heard
        // of QueryWatt, with no network at all.
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePageCarriesItsOwnDataAsValidJson()
    {
        var html = Render(WithBaseline());
        var match = DataBlockRegex().Match(html);

        Assert.True(match.Success, "The data block is missing.");

        using var document = System.Text.Json.JsonDocument.Parse(match.Groups["json"].Value);
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("scopeCount").GetInt32());
        Assert.Equal("orders.by-customer", root.GetProperty("rows")[0].GetProperty("queryId").GetString());
    }

    [Fact]
    public void ARegressionIsNamedInTheHeadlineAndCarriesItsBeforeAndAfter()
    {
        var model = WithBaseline();

        Assert.Equal(1, model.FailureCount);
        Assert.Contains("regressed", model.Headline, StringComparison.Ordinal);

        var regressed = model.Rows.Single(row => row.Verdict == nameof(InAppVerdict.Regression));

        Assert.Equal(200, regressed.BaselineLogicalReads);
        Assert.Equal(2_000, regressed.LogicalReads);
        Assert.Contains("Reads per row rose", regressed.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutABaselineThePageStillReportsWhatTheRunDid()
    {
        var model = HtmlReportModelFactory.Create(Observation(), verification: null, "test");

        Assert.False(model.HasBaseline);
        Assert.Equal("Observed", model.Headline);
        Assert.All(model.Rows, row => Assert.Null(row.Verdict));

        var html = Render(model);

        Assert.Contains("orders.by-customer", html, StringComparison.Ordinal);
    }

    [Fact]
    public void DarkModeIsDeclaredForBothTheSystemSettingAndTheToggle()
    {
        var html = Render(WithBaseline());

        Assert.Contains("prefers-color-scheme: dark", html, StringComparison.Ordinal);
        Assert.Contains(":root:not([data-theme=\"light\"])", html, StringComparison.Ordinal);
        Assert.Contains(":root[data-theme=\"dark\"]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AVerdictIsNeverCarriedByColourAlone()
    {
        var html = Render(WithBaseline());

        // Each verdict ships a mark beside its name, so the page survives a greyscale print and a
        // colour-blind reader.
        Assert.Contains("VERDICTS", html, StringComparison.Ordinal);
        Assert.Contains("mark", html, StringComparison.Ordinal);
        Assert.Contains("\u2715", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedCommandReachesThePage()
    {
        var model = WithBaseline();
        var repetition = Assert.Single(model.Repetitions);

        Assert.Equal(12, repetition.Repetitions);

        var html = Render(model);

        Assert.Contains("Repeated commands", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AngleBracketsInTheDataCannotCloseTheScriptElement()
    {
        var records = new[]
        {
            Scope("orders.<script>alert(1)</script>", Command("SELECT 1 FROM t WHERE a < 2", 10, 1))
        };

        var html = Render(HtmlReportModelFactory.Create(
            ObservationReportFactory.Create(records),
            verification: null,
            "test"));

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("\\u003cscript\\u003e", html, StringComparison.Ordinal);
    }

    private static string Render(HtmlReportModel model) => HtmlReportRenderer.Render(model);

    private static HtmlReportModel WithBaseline()
    {
        var observation = Observation();

        var baseline = new InAppBaselineDocument(
            InAppBaselineContract.SchemaVersion,
            "test",
            DateTimeOffset.UnixEpoch,
            [
                new InAppBaselineEntry(
                    "orders.by-customer",
                    "p[]",
                    "AdoNet",
                    DateTimeOffset.UnixEpoch,
                    1,
                    new InAppMetrics(200, 1, 100, 5d, 2d)),
                new InAppBaselineEntry(
                    "GET /orders",
                    "p[]",
                    "AdoNet",
                    DateTimeOffset.UnixEpoch,
                    1,
                    new InAppMetrics(600, 1, 60, 5d, 10d))
            ]);

        var verification = VerdictMatrix.Compare(
            baseline,
            InAppBaselineFactory.Summarise(Records()));

        return HtmlReportModelFactory.Create(observation, verification, "test");
    }

    private static ObservationReport Observation() => ObservationReportFactory.Create(Records());

    private static IReadOnlyList<MeasurementRecord> Records()
    {
        var lineItems = Enumerable
            .Range(1, 12)
            .Select(ordinal => Command("SELECT Sku FROM dbo.OrderLine WHERE OrderId = @OrderId", 50, 5, ordinal))
            .ToArray();

        return
        [
            // Twice the reads per row of its baseline: a regression.
            Scope("orders.by-customer", Command("SELECT * FROM dbo.[Order]", 2_000, 100)),
            Scope("GET /orders", [Command("SELECT * FROM dbo.[Order]", 60, 12), .. lineItems])
        ];
    }

    private static MeasurementRecord Scope(string queryId, params MeasuredCommand[] commands) =>
        new(
            queryId,
            "p[]",
            MeasurementSource.AdoNet,
            MeasurementStatus.Completed,
            0,
            null,
            DateTimeOffset.UnixEpoch,
            9.5d,
            commands,
            [],
            []);

    private static MeasuredCommand Command(string text, long reads, long rows, int ordinal = 1) =>
        new(ordinal, text, CommandType.Text, MeasurementSource.AdoNet, [], CommandOutcome.Succeeded)
        {
            ClientDurationMilliseconds = 1.25d,
            LogicalReads = reads,
            CpuTimeMilliseconds = 1L,
            RowsReturned = rows
        };

    [GeneratedRegex("""<script type="application/json" id="qw-data">(?<json>.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex DataBlockRegex();
}
