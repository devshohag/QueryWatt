using QueryWatt.Baselines;
using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class BaselineJsonStoreTests
{
    [Fact]
    public void SaveAndLoad_PreservesSchemaAndUsesStableBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"querywatt-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "baseline.json");
            var document = CreateDocument();
            var store = new BaselineJsonStore();

            store.Save(path, document);
            var firstBytes = File.ReadAllBytes(path);
            var loaded = store.Load(path);
            store.Save(path, loaded);
            var secondBytes = File.ReadAllBytes(path);

            Assert.Equal(BaselineContract.SchemaVersion, loaded.SchemaVersion);
            Assert.Equal(firstBytes, secondBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BaselineDocument CreateDocument()
    {
        var metric = MeasurementStatistics.SummarizeMetric(Enumerable.Repeat(10d, 20));
        var summary = new QueryStatisticsSummary(
            "query",
            metric,
            metric,
            metric,
            metric,
            metric,
            metric,
            metric,
            metric,
            metric);

        return new BaselineDocument(
            BaselineContract.SchemaVersion,
            BaselineContract.ToolVersion,
            new BaselineEnvironmentFingerprint("16", "Developer", "image", "set", "seed", 3, 20),
            [new BaselineQuery(
                "query",
                "command",
                "parameters",
                new QueryThresholds(new RegressionThreshold(25, 1000)),
                [new StatementPlanFingerprint(1, "query-hash", "plan-hash")],
                summary,
                [])]);
    }
}
