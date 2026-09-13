using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using QueryWatt.Core;

namespace QueryWatt.SqlServer;

public sealed class SqlServerMeasurementRunner : IMeasurementRunner
{
    public const string PinnedSetOptions = """
        SET ANSI_NULLS ON;
        SET ANSI_PADDING ON;
        SET ANSI_WARNINGS ON;
        SET ARITHABORT ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET QUOTED_IDENTIFIER ON;
        SET NUMERIC_ROUNDABORT OFF;
        SET NOCOUNT OFF;
        SET STATISTICS IO ON;
        SET STATISTICS TIME ON;
        """;

    private readonly string _connectionString;
    private readonly StatisticsMessageParser _parser;

    public SqlServerMeasurementRunner(
        string connectionString,
        StatisticsMessageParser? parser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Pooling = false
        };

        _connectionString = builder.ConnectionString;
        _parser = parser ?? new StatisticsMessageParser();
    }

    public async Task<QueryMeasurementSample> MeasureAsync(
        MeasurementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        await using var connection = new SqlConnection(_connectionString);
        var collector = new SqlMessageCollector();
        connection.InfoMessage += collector.Handle;

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPinnedOptionsAsync(connection, cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < request.WarmupRuns; index++)
            {
                collector.BeginRun();
                _ = await ExecuteAndDrainAsync(connection, request, cancellationToken).ConfigureAwait(false);
                collector.DiscardRun();
            }

            var runs = new List<RunMetrics>(request.MeasuredRuns);
            for (var index = 0; index < request.MeasuredRuns; index++)
            {
                collector.BeginRun();
                var execution = await ExecuteAndDrainAsync(connection, request, cancellationToken)
                    .ConfigureAwait(false);
                var messages = collector.CompleteRun();
                var parsed = _parser.Parse(messages);

                runs.Add(new RunMetrics(
                    index + 1,
                    parsed.LogicalReads,
                    parsed.LobLogicalReads,
                    parsed.PhysicalReads,
                    parsed.ReadAheadReads,
                    parsed.LobPhysicalReads,
                    parsed.LobReadAheadReads,
                    parsed.CpuTimeMilliseconds,
                    execution.Duration.TotalMilliseconds,
                    execution.RowsReturned,
                    parsed.Statements,
                    messages));
            }

            return new QueryMeasurementSample(request.Name, request.WarmupRuns, runs);
        }
        finally
        {
            connection.InfoMessage -= collector.Handle;
        }
    }

    private static async Task ApplyPinnedOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PinnedSetOptions;
        command.CommandType = CommandType.Text;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExecutionResult> ExecuteAndDrainAsync(
        SqlConnection connection,
        MeasurementRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = request.CommandText;
        command.CommandType = request.CommandType;
        command.CommandTimeout = request.CommandTimeoutSeconds;

        AddParameters(command, request.EffectiveParameters);

        long rowsReturned = 0;
        var stopwatch = Stopwatch.StartNew();

        await using (var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken)
            .ConfigureAwait(false))
        {
            do
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rowsReturned++;
                }
            }
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
        }

        stopwatch.Stop();
        return new ExecutionResult(stopwatch.Elapsed, rowsReturned);
    }

    private static void AddParameters(SqlCommand command, IReadOnlyList<QueryParameter> parameters)
    {
        foreach (var definition in parameters)
        {
            var parameter = new SqlParameter
            {
                ParameterName = definition.Name,
                DbType = definition.Type
            };
            parameter.Value = definition.Value ?? DBNull.Value;

            if (definition.Size is not null)
            {
                parameter.Size = definition.Size.Value;
            }

            if (definition.Precision is not null)
            {
                parameter.Precision = definition.Precision.Value;
            }

            if (definition.Scale is not null)
            {
                parameter.Scale = definition.Scale.Value;
            }

            command.Parameters.Add(parameter);
        }
    }

    private sealed record ExecutionResult(TimeSpan Duration, long RowsReturned);

    private sealed class SqlMessageCollector
    {
        private readonly object _gate = new();
        private List<string>? _activeMessages;

        public void BeginRun()
        {
            lock (_gate)
            {
                _activeMessages = [];
            }
        }

        public void Handle(object sender, SqlInfoMessageEventArgs args)
        {
            lock (_gate)
            {
                if (_activeMessages is null)
                {
                    return;
                }

                foreach (SqlError error in args.Errors)
                {
                    _activeMessages.Add(error.Message);
                }
            }
        }

        public IReadOnlyList<string> CompleteRun()
        {
            lock (_gate)
            {
                var completed = _activeMessages?.ToArray()
                    ?? throw new InvalidOperationException("No active measurement run exists.");
                _activeMessages = null;
                return completed;
            }
        }

        public void DiscardRun()
        {
            lock (_gate)
            {
                _activeMessages = null;
            }
        }
    }
}
