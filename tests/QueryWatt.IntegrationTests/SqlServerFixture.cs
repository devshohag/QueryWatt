using System.Data;
using Microsoft.Data.SqlClient;
using QueryWatt.Core.Instrumentation;
using QueryWatt.SqlServer.Capture;
using QueryWatt.SqlServer.Wrapping;
using Testcontainers.MsSql;
using Xunit;

namespace QueryWatt.IntegrationTests;

/// <summary>
/// One SQL Server container, one deterministic data set, one capture subscription, shared by
/// every stack's tests. The container makes the data set fixed, which is what makes two
/// measurements of the same query comparable.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private SqlClientCapture? _capture;

    public string ConnectionString { get; private set; } = string.Empty;

    public InMemoryMeasurementSink Sink { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            TrustServerCertificate = true
        };

        ConnectionString = builder.ConnectionString;

        await CreateSchemaAsync().ConfigureAwait(false);

        _capture = SqlClientCapture.Enable(new SqlServerCaptureOptions { KeepRawMessages = true });
        Watt.Sink = Sink;
    }

    public async Task DisposeAsync()
    {
        Watt.Reset();
        _capture?.Dispose();
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    public SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Opens a connection wrapped so its readers finish their result streams before closing, which
    /// is what lets the server's statistics reach QueryWatt for consumers that do not drain.
    /// </summary>
    public QueryWattConnection OpenWrappedConnection()
    {
        var connection = QueryWattConnection.Wrap(new SqlConnection(ConnectionString));
        connection.Open();
        return connection;
    }

    /// <summary>Runs an operation with measurement off, to capture the reference result.</summary>
    public T WithoutMeasurement<T>(Func<T> operation)
    {
        Watt.Instrumentation = InstrumentationLevel.Off;
        try
        {
            return operation();
        }
        finally
        {
            Watt.Instrumentation = InstrumentationLevel.Off;
        }
    }

    /// <inheritdoc cref="WithoutMeasurement{T}(Func{T})"/>
    public async Task<T> WithoutMeasurementAsync<T>(Func<Task<T>> operation)
    {
        Watt.Instrumentation = InstrumentationLevel.Off;
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            Watt.Instrumentation = InstrumentationLevel.Off;
        }
    }

    /// <summary>Runs an operation inside a measured scope and returns the result with its record.</summary>
    public Measured<T> Measure<T>(
        string queryId,
        Func<T> operation,
        InstrumentationLevel level = InstrumentationLevel.Full)
    {
        Sink.Clear();
        Watt.Instrumentation = level;

        try
        {
            T result;
            MeasurementRecord? record;

            using (var scope = Watt.Measure(queryId))
            {
                result = operation();
                scope.Complete();
                record = scope.Record;
            }

            return new Measured<T>(result, record!);
        }
        finally
        {
            Watt.Instrumentation = InstrumentationLevel.Off;
        }
    }

    /// <inheritdoc cref="Measure{T}(string, Func{T}, InstrumentationLevel)"/>
    public async Task<Measured<T>> MeasureAsync<T>(
        string queryId,
        Func<Task<T>> operation,
        InstrumentationLevel level = InstrumentationLevel.Full)
    {
        Sink.Clear();
        Watt.Instrumentation = level;

        try
        {
            T result;
            MeasurementRecord? record;

            using (var scope = Watt.Measure(queryId))
            {
                result = await operation().ConfigureAwait(false);
                scope.Complete();
                record = scope.Record;
            }

            return new Measured<T>(result, record!);
        }
        finally
        {
            Watt.Instrumentation = InstrumentationLevel.Off;
        }
    }

    private async Task CreateSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await ExecuteAsync(connection, TestSchema.Script).ConfigureAwait(false);
        await ExecuteAsync(connection, TestSchema.Procedures).ConfigureAwait(false);
        await ExecuteAsync(connection, TestSchema.WriteProcedure).ConfigureAwait(false);
        await ExecuteAsync(connection, TestSchema.GuardProbe).ConfigureAwait(false);

        await using var seed = connection.CreateCommand();
        seed.CommandText = TestSchema.Seed;
        seed.CommandTimeout = 120;
        seed.Parameters.Add("@PrimaryCustomerId", SqlDbType.UniqueIdentifier).Value =
            TestSchema.PrimaryCustomerId;
        seed.Parameters.Add("@SecondaryCustomerId", SqlDbType.UniqueIdentifier).Value =
            TestSchema.SecondaryCustomerId;
        seed.Parameters.Add("@OrdersPerCustomer", SqlDbType.Int).Value = TestSchema.OrdersPerCustomer;
        seed.Parameters.Add("@ProductCount", SqlDbType.Int).Value = TestSchema.ProductCount;
        seed.Parameters.Add("@WriteProbeRows", SqlDbType.Int).Value = TestSchema.WriteProbeRows;
        seed.Parameters.Add("@SeedEpoch", SqlDbType.DateTime2).Value = TestSchema.SeedEpoch;
        await seed.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

/// <summary>An operation's result paired with the measurement it produced.</summary>
public sealed record Measured<T>(T Result, MeasurementRecord Record);

[CollectionDefinition(SqlServerCollection.Name, DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "querywatt-sqlserver";
}
