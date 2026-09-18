using System.Collections;
using System.Data;
using Microsoft.Data.SqlClient;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Per-connection state for in-app capture: the info-message buffer, client statistics, and the
/// <c>SET STATISTICS IO/TIME</c> options. Contract v2 §6.
/// </summary>
/// <remarks>
/// Two facts shape this class. First, a pooled connection is reset by <c>sp_reset_connection</c>
/// when it is reused, which clears <c>SET</c> state — so the options are applied per logical open,
/// not once per object. Second, the session language is <em>checked and never changed</em>:
/// <c>SET LANGUAGE</c> on a connection QueryWatt does not own would alter date interpretation for
/// application code sharing the pool.
/// </remarks>
internal sealed class ConnectionInstrumentation
{
    private const int MaxBufferedMessages = 20_000;

    private readonly SqlConnection _connection;
    private readonly Lock _gate = new();
    private readonly List<string> _messages = [];

    private bool _handlersAttached;
    private bool _appliedForCurrentOpen;

    public ConnectionInstrumentation(SqlConnection connection) => _connection = connection;

    /// <summary>True once <c>STATISTICS IO/TIME</c> is on and the session language is parseable.</summary>
    public bool ServerStatisticsAvailable { get; private set; }

    /// <summary>Set when statistics cannot be collected, with the reason to report.</summary>
    public MeasurementDiagnostic? Unavailable { get; private set; }

    public int MessageCount
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    public IReadOnlyList<string> Slice(int from, int to)
    {
        lock (_gate)
        {
            var start = Math.Clamp(from, 0, _messages.Count);
            var end = to < 0 ? _messages.Count : Math.Clamp(to, start, _messages.Count);
            return _messages.GetRange(start, end - start);
        }
    }

    public void ResetBuffer()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }

    /// <summary>
    /// Attaches handlers, applies the session options when they are missing, and resets the
    /// client counters so the next command's statistics are its own.
    /// </summary>
    public void Prepare(SqlCommand command, InstrumentationLevel level, bool enableSetOptions)
    {
        AttachHandlers();

        if (_connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            _connection.StatisticsEnabled = true;
            _connection.ResetStatistics();
        }
        catch (Exception exception)
        {
            Unavailable ??= new MeasurementDiagnostic(
                DiagnosticCode.MarsAttributionUnsafe,
                "Client statistics are unavailable on this connection: " + exception.Message);
        }

        if (level != InstrumentationLevel.Full || !enableSetOptions || _appliedForCurrentOpen)
        {
            return;
        }

        _appliedForCurrentOpen = true;
        ApplySessionOptions(command);
    }

    public IDictionary? RetrieveStatistics()
    {
        try
        {
            return _connection.RetrieveStatistics();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void AttachHandlers()
    {
        if (_handlersAttached)
        {
            return;
        }

        _handlersAttached = true;
        _connection.InfoMessage += OnInfoMessage;
        _connection.StateChange += OnStateChange;
    }

    private void OnStateChange(object sender, StateChangeEventArgs args)
    {
        // A pooled connection returning and being handed out again clears SET state, so the
        // options have to be reapplied on the next command.
        if (args.CurrentState is ConnectionState.Open or ConnectionState.Closed)
        {
            _appliedForCurrentOpen = false;
            ServerStatisticsAvailable = false;
        }
    }

    private void OnInfoMessage(object sender, SqlInfoMessageEventArgs args)
    {
        lock (_gate)
        {
            if (_messages.Count >= MaxBufferedMessages)
            {
                return;
            }

            foreach (SqlError error in args.Errors)
            {
                _messages.Add(error.Message);
            }
        }
    }

    private void ApplySessionOptions(SqlCommand command)
    {
        try
        {
            if (!IsEnglishSession(command))
            {
                ServerStatisticsAvailable = false;
                Unavailable = new MeasurementDiagnostic(
                    DiagnosticCode.NonEnglishSessionLanguage,
                    "Logical reads and CPU are unavailable because this session's language is not "
                    + "us_english, and QueryWatt does not change the language of a connection it "
                    + "does not own. Set the measurement login's default language to us_english.");
                return;
            }

            using var setOptions = _connection.CreateCommand();
            setOptions.Transaction = command.Transaction;
            setOptions.CommandType = CommandType.Text;
            setOptions.CommandText = "SET STATISTICS IO ON; SET STATISTICS TIME ON;";
            setOptions.ExecuteNonQuery();

            ServerStatisticsAvailable = true;
            Unavailable = null;
        }
        catch (Exception exception)
        {
            ServerStatisticsAvailable = false;
            Unavailable = new MeasurementDiagnostic(
                DiagnosticCode.PlanCaptureSkipped,
                "Session statistics could not be enabled on this connection: " + exception.Message);
        }
    }

    private bool IsEnglishSession(SqlCommand command)
    {
        using var probe = _connection.CreateCommand();
        probe.Transaction = command.Transaction;
        probe.CommandType = CommandType.Text;
        probe.CommandText = "SELECT CAST(@@LANGID AS int);";

        return probe.ExecuteScalar() is int languageId && languageId == 0;
    }
}
