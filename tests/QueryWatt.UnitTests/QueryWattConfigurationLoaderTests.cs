using System.Data;
using QueryWatt.Configuration;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class QueryWattConfigurationLoaderTests
{
    [Fact]
    public void Load_ResolvesMultipleQueriesAndTypedParameters()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteQuery("queries/one.sql", "SELECT @Count");
        fixture.WriteQuery("queries/two.sql", "SELECT @CustomerId");
        fixture.WriteQuery("seed.sql", "SELECT 1");
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 1
            environment:
              seedScripts:
                - seed.sql
              tables:
                - dbo.TestTable
            connection:
              environmentVariable: TEST_CONNECTION
            measurement:
              warmupRuns: 2
              measuredRuns: 20
              commandTimeoutSeconds: 45
            thresholds:
              logicalReads:
                percent: 25
                absolute: 1000
            queries:
              - name: first
                file: queries/one.sql
                parameters:
                  - name: Count
                    type: int32
                    value: "42"
              - name: second
                file: queries/two.sql
                parameters:
                  - name: CustomerId
                    type: guid
                    value: "5419f84b-a513-4f1b-b784-5c216edf79ee"
            """);

        var result = new QueryWattConfigurationLoader().Load(configPath);

        Assert.Equal("TEST_CONNECTION", result.ConnectionStringEnvironmentVariable);
        Assert.Equal(
            new[] { "first", "second" },
            result.Requests.Select(request => request.Name).ToArray());
        Assert.Equal(42, result.Requests[0].EffectiveParameters[0].Value);
        Assert.Equal(DbType.Int32, result.Requests[0].EffectiveParameters[0].Type);
        Assert.IsType<Guid>(result.Requests[1].EffectiveParameters[0].Value);
        Assert.All(result.Requests, request => Assert.Equal(20, request.MeasuredRuns));
    }

    [Fact]
    public void Load_RejectsDuplicateQueryNamesIgnoringCase()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteQuery("query.sql", "SELECT 1");
        fixture.WriteQuery("seed.sql", "SELECT 1");
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 1
            environment:
              seedScripts:
                - seed.sql
              tables:
                - dbo.TestTable
            queries:
              - name: Seek
                file: query.sql
              - name: seek
                file: query.sql
            """);

        var exception = Assert.Throws<ConfigurationException>(() =>
            new QueryWattConfigurationLoader().Load(configPath));

        Assert.Contains("unique", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsUnsupportedSchemaVersion()
    {
        using var fixture = new ConfigurationFixture();
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 99
            queries:
              - name: seek
                file: missing.sql
            """);

        var exception = Assert.Throws<ConfigurationException>(() =>
            new QueryWattConfigurationLoader().Load(configPath));

        Assert.Contains("schemaVersion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsUnknownYamlProperty()
    {
        using var fixture = new ConfigurationFixture();
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 1
            typoProperty: true
            queries:
              - name: seek
                file: query.sql
            """);

        var exception = Assert.Throws<ConfigurationException>(() =>
            new QueryWattConfigurationLoader().Load(configPath));

        Assert.Contains("Invalid YAML", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsEnabledEnergyWithoutCoefficient()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteQuery("query.sql", "SELECT 1");
        fixture.WriteQuery("seed.sql", "SELECT 1");
        var configPath = fixture.WriteConfiguration("""
            schemaVersion: 1
            environment:
              seedScripts:
                - seed.sql
              tables:
                - dbo.TestTable
            energy:
              enabled: true
              wattsPerBusyCore: null
            queries:
              - name: query
                file: query.sql
            """);

        var exception = Assert.Throws<ConfigurationException>(() =>
            new QueryWattConfigurationLoader().Load(configPath));

        Assert.Contains("wattsPerBusyCore", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"querywatt-tests-{Guid.NewGuid():N}");

        public ConfigurationFixture() => Directory.CreateDirectory(_root);

        public string WriteQuery(string relativePath, string contents)
        {
            var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
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
