using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace QueryWatt.Core.Instrumentation;

/// <summary>
/// A live measurement scope. Provider adapters write observed commands here; the developer-facing
/// lifecycle lives on <see cref="MeasurementScope"/>. Contract v2 §3, §4.
/// </summary>
public sealed class MeasurementScopeState
{
    private readonly List<MeasuredCommand> _commands = [];
    private readonly List<MeasurementRecord> _children = [];
    private readonly List<MeasurementDiagnostic> _diagnostics = [];
    private readonly List<DbCommand> _explicitAttachments = [];
    private readonly IMeasurementSink _sink;
    private readonly long _startTimestamp;
    private readonly Lock _gate = new();

    private int _startedCommands;
    private int _openReaders;
    private string? _explicitScenario;
    private long? _callerReportedRows;
    private Exception? _exception;
    private bool _published;

    internal MeasurementScopeState(
        string queryId,
        MeasurementScopeState? parent,
        int depth,
        InstrumentationLevel level,
        IMeasurementSink sink)
    {
        QueryId = queryId;
        Parent = parent;
        Depth = depth;
        Level = level;
        _sink = sink;
        _startTimestamp = Stopwatch.GetTimestamp();
        StartedUtc = DateTimeOffset.UtcNow;
    }

    public string QueryId { get; }

    public MeasurementScopeState? Parent { get; }

    public int Depth { get; }

    public InstrumentationLevel Level { get; }

    public DateTimeOffset StartedUtc { get; }

    public MeasurementStatus Status { get; private set; } = MeasurementStatus.Running;

    /// <summary>The finished record, available once the scope reached a terminal state.</summary>
    public MeasurementRecord? Record { get; private set; }

    public bool IsTerminal => Status != MeasurementStatus.Running;

    // ── provider adapter surface ─────────────────────────────────────────────

    /// <summary>Reserve the next command ordinal. Called by an adapter before execution.</summary>
    public int BeginCommand()
    {
        lock (_gate)
        {
            _startedCommands++;
            return _startedCommands;
        }
    }

    /// <summary>Record a command that finished. Called by an adapter after execution.</summary>
    public void RecordCommand(MeasuredCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            _commands.Add(command);
        }
    }

    public void ReaderOpened()
    {
        lock (_gate)
        {
            _openReaders++;
        }
    }

    public void ReaderClosed()
    {
        lock (_gate)
        {
            if (_openReaders > 0)
            {
                _openReaders--;
            }
        }
    }

    public void AddDiagnostic(DiagnosticCode code, string message)
    {
        lock (_gate)
        {
            _diagnostics.Add(new MeasurementDiagnostic(code, message));
        }
    }

    /// <summary>
    /// Explicit attribution fallback for providers that emit no diagnostic events. Identity is
    /// captured immediately; metrics stay null unless a capture layer fills them in.
    /// Contract v2 §2.1.
    /// </summary>
    public void AttachCommand(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            _explicitAttachments.Add(command);
        }
    }

    // ── lifecycle ────────────────────────────────────────────────────────────

    internal void SetScenario(string name)
    {
        lock (_gate)
        {
            _explicitScenario = name;
        }
    }

    internal void Complete(long? rowsReturned)
    {
        lock (_gate)
        {
            if (_published)
            {
                return;
            }

            _callerReportedRows = rowsReturned ?? _callerReportedRows;

            if (_openReaders > 0)
            {
                _diagnostics.Add(new MeasurementDiagnostic(
                    DiagnosticCode.OpenReaderAtComplete,
                    $"Complete() was called on '{QueryId}' with {_openReaders} reader(s) from this "
                    + "scope still open. Metrics for those commands may be incomplete."));
            }

            Finish(_startedCommands > _commands.Count
                ? MeasurementStatus.Abandoned
                : MeasurementStatus.Completed);
        }
    }

    internal void Fail(Exception exception)
    {
        lock (_gate)
        {
            if (_published)
            {
                return;
            }

            _exception = exception;
            Finish(exception is OperationCanceledException
                ? MeasurementStatus.Cancelled
                : MeasurementStatus.Failed);
        }
    }

    internal void Abandon()
    {
        lock (_gate)
        {
            if (_published)
            {
                return;
            }

            _diagnostics.Add(new MeasurementDiagnostic(
                DiagnosticCode.AbandonedScope,
                $"Scope '{QueryId}' was disposed without Complete() or Fail(). It is reported but "
                + "never written to a baseline."));

            Finish(MeasurementStatus.Abandoned);
        }
    }

    private void Finish(MeasurementStatus status)
    {
        Status = status;
        _published = true;

        AbsorbExplicitAttachments();

        if (_commands.Count == 0 && _children.Count == 0)
        {
            _diagnostics.Add(new MeasurementDiagnostic(
                DiagnosticCode.NoCommandsInScope,
                $"Scope '{QueryId}' executed no database command. The measurement block is "
                + "probably around the wrong method."));
        }

        var record = new MeasurementRecord(
            QueryId,
            ResolveScenarioKey(),
            ResolveSource(),
            status,
            Depth,
            Parent?.QueryId,
            StartedUtc,
            Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds,
            _commands.ToArray(),
            _children.ToArray(),
            _diagnostics.ToArray())
        {
            ExceptionType = _exception?.GetType().FullName,
            ExceptionMessage = _exception?.Message,
            CallerReportedRows = _callerReportedRows
        };

        Record = record;
        Parent?.AddChild(record);

        try
        {
            _sink.Publish(record);
        }
        catch
        {
            // A sink fault must never reach the application.
        }
    }

    private void AbsorbExplicitAttachments()
    {
        foreach (var command in _explicitAttachments)
        {
            if (_commands.Any(recorded => recorded.CommandText == command.CommandText))
            {
                continue;
            }

            _commands.Add(new MeasuredCommand(
                _commands.Count + 1,
                command.CommandText ?? string.Empty,
                command.CommandType,
                MeasurementSource.AdoNet,
                DescribeParameters(command),
                CommandOutcome.Unknown));
        }

        _explicitAttachments.Clear();
    }

    private static IReadOnlyList<CommandParameterInfo> DescribeParameters(DbCommand command)
    {
        var described = new List<CommandParameterInfo>(command.Parameters.Count);

        foreach (DbParameter parameter in command.Parameters)
        {
            described.Add(new CommandParameterInfo(
                parameter.ParameterName ?? string.Empty,
                SafeDbType(parameter),
                parameter.Value is null or DBNull,
                parameter.Size == 0 ? null : parameter.Size,
                parameter.Precision == 0 ? null : parameter.Precision,
                parameter.Scale == 0 ? null : parameter.Scale));
        }

        return described;
    }

    private static DbType SafeDbType(DbParameter parameter)
    {
        try
        {
            return parameter.DbType;
        }
        catch (Exception)
        {
            // Some providers throw when DbType was never inferred. Identity is worth more than
            // a crash inside measurement code.
            return DbType.Object;
        }
    }

    private void AddChild(MeasurementRecord child)
    {
        lock (_gate)
        {
            _children.Add(child);
        }
    }

    private string ResolveScenarioKey()
    {
        if (_explicitScenario is not null)
        {
            return ScenarioKey.FromName(_explicitScenario);
        }

        if (_commands.Count == 0)
        {
            return ScenarioKey.None;
        }

        return ScenarioKey.FromParameters(_commands.SelectMany(command => command.Parameters));
    }

    private MeasurementSource ResolveSource()
    {
        var sources = _commands
            .Select(command => command.Source)
            .Where(source => source != MeasurementSource.Unknown)
            .Distinct()
            .ToArray();

        return sources.Length == 1 ? sources[0] : MeasurementSource.Unknown;
    }
}
