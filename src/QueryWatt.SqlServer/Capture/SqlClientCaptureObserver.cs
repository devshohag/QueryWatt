using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Turns SqlClient diagnostic events into commands on the innermost open measurement scope. A
/// command executed outside every scope is ignored entirely — QueryWatt never guesses which
/// queries matter. Contract v2 §2.
/// </summary>
internal sealed class SqlClientCaptureObserver : IObserver<KeyValuePair<string, object?>>
{
    private const string BeforeSuffix = ".WriteCommandBefore";
    private const string AfterSuffix = ".WriteCommandAfter";
    private const string ErrorSuffix = ".WriteCommandError";

    private readonly SqlServerCaptureOptions _options;
    private readonly ConnectionInstrumentationRegistry _connections = new();
    private readonly ConcurrentDictionary<object, PendingCommand> _inFlight = new();
    private readonly ConditionalWeakTable<MeasurementScopeState, List<PendingCommand>> _byScope = new();
    private readonly StatisticsMessageParser _parser = new();

    public SqlClientCaptureObserver(SqlServerCaptureOptions options) => _options = options;

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            if (value.Key.EndsWith(BeforeSuffix, StringComparison.Ordinal))
            {
                OnBefore(value.Value);
            }
            else if (value.Key.EndsWith(AfterSuffix, StringComparison.Ordinal))
            {
                OnFinished(value.Value, exception: null);
            }
            else if (value.Key.EndsWith(ErrorSuffix, StringComparison.Ordinal))
            {
                OnFinished(value.Value, PayloadReader.Read<Exception>(value.Value, "Exception"));
            }
        }
        catch (Exception)
        {
            // Capture is best effort. It may never change what the application does.
        }
    }

    private void OnBefore(object? payload)
    {
        var scope = MeasurementContext.Current;
        if (scope is null || scope.IsTerminal)
        {
            return;
        }

        var command = PayloadReader.Read<DbCommand>(payload, "Command");
        if (command is null)
        {
            return;
        }

        var instrumentation = _connections.For(command.Connection);
        var pendingForScope = PendingFor(scope);

        var firstOnThisConnection = true;
        lock (pendingForScope)
        {
            foreach (var existing in pendingForScope)
            {
                if (ReferenceEquals(existing.Instrumentation, instrumentation))
                {
                    firstOnThisConnection = false;
                    break;
                }
            }
        }

        if (instrumentation is not null)
        {
            if (firstOnThisConnection)
            {
                instrumentation.ResetBuffer();
            }

            if (command is Microsoft.Data.SqlClient.SqlCommand sqlCommand)
            {
                instrumentation.Prepare(
                    sqlCommand,
                    scope.Level,
                    _options.EnableConnectionInstrumentation);
            }
        }

        var pending = new PendingCommand
        {
            Ordinal = scope.BeginCommand(),
            CommandText = command.CommandText ?? string.Empty,
            CommandType = command.CommandType,
            Parameters = SqlParameterDescriber.Describe(command),
            Instrumentation = instrumentation,
            MessageStart = instrumentation?.MessageCount ?? 0,
            StartTimestamp = Stopwatch.GetTimestamp()
        };

        lock (pendingForScope)
        {
            pendingForScope.Add(pending);
        }

        var correlationKey = KeyFor(payload, command);
        pending.CorrelationKey = correlationKey;
        _inFlight[correlationKey] = pending;
    }

    private void OnFinished(object? payload, Exception? exception)
    {
        var command = PayloadReader.Read<DbCommand>(payload, "Command");
        if (command is null)
        {
            return;
        }

        if (!_inFlight.TryRemove(KeyFor(payload, command), out var pending))
        {
            return;
        }

        pending.DurationMilliseconds = Stopwatch
            .GetElapsedTime(pending.StartTimestamp)
            .TotalMilliseconds;
        pending.Exception = exception;
        pending.ClientStatistics = pending.Instrumentation?.RetrieveStatistics();
        pending.Finished = true;
    }

    private List<PendingCommand> PendingFor(MeasurementScopeState scope) =>
        _byScope.GetValue(scope, key =>
        {
            key.AddFinalizer(FinalizeScope);
            return [];
        });

    /// <summary>
    /// Runs while the scope is closing: slices the buffered statistics messages per command,
    /// parses them, and records the commands. Contract v2 §6.
    /// </summary>
    private void FinalizeScope(MeasurementScopeState scope)
    {
        if (!_byScope.TryGetValue(scope, out var pendingForScope))
        {
            return;
        }

        _byScope.Remove(scope);

        PendingCommand[] ordered;
        lock (pendingForScope)
        {
            ordered = pendingForScope.OrderBy(pending => pending.Ordinal).ToArray();
        }

        var reportedUnavailable = new HashSet<DiagnosticCode>();
        var reportedInlineLiterals = false;

        foreach (var group in ordered.GroupBy(pending => pending.Instrumentation))
        {
            var commands = group.ToArray();

            for (var index = 0; index < commands.Length; index++)
            {
                var pending = commands[index];
                var instrumentation = pending.Instrumentation;

                if (pending.CorrelationKey is not null)
                {
                    _inFlight.TryRemove(pending.CorrelationKey, out _);
                }

                if (!pending.Finished)
                {
                    // The driver never reported this command finishing, so there is nothing
                    // trustworthy to record. The scope's started-versus-recorded check turns this
                    // into Abandoned. Contract v2 §3.1.
                    continue;
                }

                // The last command on a connection owns every message to the end of the buffer,
                // because a reader's statistics arrive after the driver's "after" event.
                var messageEnd = index + 1 < commands.Length ? commands[index + 1].MessageStart : -1;
                var messages = instrumentation?.Slice(pending.MessageStart, messageEnd)
                               ?? Array.Empty<string>();

                if (instrumentation is not null
                    && commands.Length == 1
                    && instrumentation.RetrieveStatistics() is { } refreshed)
                {
                    // One command on this connection, so the counters cannot be confused with
                    // another command's. Re-read them now that the reader is drained.
                    pending.ClientStatistics = refreshed;
                }

                var serverStatistics = ParseStatistics(scope, instrumentation, messages);

                if (instrumentation?.Unavailable is { } unavailable
                    && reportedUnavailable.Add(unavailable.Code))
                {
                    scope.AddDiagnostic(unavailable.Code, unavailable.Message);
                }

                if (!reportedInlineLiterals
                    && CommandTextInspector.ContainsInlineLiterals(pending.CommandText))
                {
                    reportedInlineLiterals = true;
                    scope.AddDiagnostic(
                        DiagnosticCode.InlineLiteralsDetected,
                        $"A command in '{scope.QueryId}' carries inline literals instead of "
                        + "parameters, so its normalized identity may change between runs.");
                }

                scope.RecordCommand(MeasuredCommandFactory.Create(
                    pending.Ordinal,
                    pending.CommandText,
                    pending.CommandType,
                    _options.DefaultSource,
                    pending.Parameters,
                    pending.DurationMilliseconds,
                    pending.Exception,
                    serverStatistics,
                    ClientCommandStatistics.From(pending.ClientStatistics),
                    _options.MaxCommandTextLength,
                    _options.KeepRawMessages ? messages : null));
            }
        }
    }

    private ParsedStatistics? ParseStatistics(
        MeasurementScopeState scope,
        ConnectionInstrumentation? instrumentation,
        IReadOnlyList<string> messages)
    {
        if (instrumentation is null || !instrumentation.ServerStatisticsAvailable || messages.Count == 0)
        {
            return null;
        }

        try
        {
            return _parser.Parse(messages);
        }
        catch (MeasurementParseException exception)
        {
            scope.AddDiagnostic(
                DiagnosticCode.MarsAttributionUnsafe,
                "Statistics messages could not be attributed to this command: " + exception.Message);
            return null;
        }
    }

    private static object KeyFor(object? payload, DbCommand command)
    {
        var operationId = PayloadReader.ReadOperationId(payload);
        return operationId == Guid.Empty ? command : operationId;
    }
}
