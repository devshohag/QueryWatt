using QueryWatt.Configuration;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class ThresholdResolutionTests
{
    [Fact]
    public void Load_MergesPerQueryThresholdsIntoTheGlobalBlockMetricByMetric()
    {
        using var fixture = new ThresholdFixture();
        fixture.WriteFile("seed.sql", "SELECT 1");
        fixture.WriteFile("query.sql", "SELECT 1");
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 1
            environment:
              seedScripts:
                - seed.sql
              tables:
                - dbo.TestTable
            thresholds:
              logicalReads:
                percent: 7
                absolute: 123
              cpuTimeMilliseconds:
                percent: 40
                absolute: 30
            queries:
              - name: inherits-everything
                file: query.sql
              - name: overrides-cpu-only
                file: query.sql
                thresholds:
                  cpuTimeMilliseconds:
                    percent: 10
                    absolute: 5
              - name: overrides-logical-reads-only
                file: query.sql
                thresholds:
                  logicalReads:
                    percent: 5
                    absolute: 50
            """);

        var result = new QueryWattConfigurationLoader().Load(configPath);

        var inherited = result.Queries[0].Thresholds;
        Assert.Equal(7d, inherited.LogicalReads.Percent);
        Assert.Equal(123d, inherited.LogicalReads.Absolute);
        Assert.Equal(40d, inherited.CpuTimeMilliseconds!.Percent);
        Assert.Equal(30d, inherited.CpuTimeMilliseconds.Absolute);

        // The regression this test exists for: a query block that tunes only CPU
        // must keep the global logical-read gate, not a hardcoded default.
        var cpuOnly = result.Queries[1].Thresholds;
        Assert.Equal(7d, cpuOnly.LogicalReads.Percent);
        Assert.Equal(123d, cpuOnly.LogicalReads.Absolute);
        Assert.Equal(10d, cpuOnly.CpuTimeMilliseconds!.Percent);
        Assert.Equal(5d, cpuOnly.CpuTimeMilliseconds.Absolute);

        var logicalReadsOnly = result.Queries[2].Thresholds;
        Assert.Equal(5d, logicalReadsOnly.LogicalReads.Percent);
        Assert.Equal(50d, logicalReadsOnly.LogicalReads.Absolute);
        Assert.Equal(40d, logicalReadsOnly.CpuTimeMilliseconds!.Percent);
        Assert.Equal(30d, logicalReadsOnly.CpuTimeMilliseconds.Absolute);
    }

    [Fact]
    public void Load_LeavesUnconfiguredSupportingMetricsUngated()
    {
        using var fixture = new ThresholdFixture();
        fixture.WriteFile("seed.sql", "SELECT 1");
        fixture.WriteFile("query.sql", "SELECT 1");
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 1
            environment:
              seedScripts:
                - seed.sql
              tables:
                - dbo.TestTable
            thresholds:
              logicalReads:
                percent: 25
                absolute: 1000
            queries:
              - name: reads-only
                file: query.sql
            """);

        var result = new QueryWattConfigurationLoader().Load(configPath);

        Assert.Null(result.Queries[0].Thresholds.CpuTimeMilliseconds);
        Assert.Null(result.Queries[0].Thresholds.ClientDurationMilliseconds);
    }

    [Fact]
    public void Load_NamesTheQueryThatHasNoLogicalReadsThresholdAnywhere()
    {
        using var fixture = new ThresholdFixture();
        fixture.WriteFile("seed.sql", "SELECT 1");
        fixture.WriteFile("query.sql", "SELECT 1");
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 1
            environment:
              seedScripts:
                - seed.sql
              tables:
                - dbo.TestTable
            thresholds:
              cpuTimeMilliseconds:
                percent: 40
                absolute: 30
            queries:
              - name: ungated
                file: query.sql
            """);

        var exception = Assert.Throws<ConfigurationException>(() =>
            new QueryWattConfigurationLoader().Load(configPath));

        Assert.Contains("ungated", exception.Message, StringComparison.Ordinal);
        Assert.Contains("logicalReads", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ThresholdFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"querywatt-threshold-tests-{Guid.NewGuid():N}");

        public ThresholdFixture() => Directory.CreateDirectory(_root);

        public void WriteFile(string relativePath, string contents)
        {
            var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public string WriteConfiguration(string contents)
        {
            var path = Path.Combine(_root, "querywatt.yml");
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
