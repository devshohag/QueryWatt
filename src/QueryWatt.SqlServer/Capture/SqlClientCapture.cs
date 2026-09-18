using System.Diagnostics;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Subscribes to the SqlClient diagnostic listener so commands the application executes inside a
/// measurement scope are observed. Nothing is wrapped: no connection, no provider factory, no
/// change to how the application creates commands. Contract v2 §2.
/// </summary>
/// <remarks>
/// Both driver families publish under the listener name <c>SqlClientDiagnosticListener</c>, with
/// event keys prefixed <c>Microsoft.Data.SqlClient.</c> or <c>System.Data.SqlClient.</c>. Events
/// are matched by suffix so both are covered. Full statistics need
/// Microsoft.Data.SqlClient, because the info-message and client-counter APIs are typed to it;
/// other drivers degrade to identity and duration.
/// </remarks>
public sealed class SqlClientCapture : IDisposable
{
    /// <summary>The listener name both SqlClient families publish under.</summary>
    public const string ListenerName = "SqlClientDiagnosticListener";

    private static readonly Lock EnableGate = new();
    private static SqlClientCapture? _current;

    private readonly List<IDisposable> _subscriptions = [];
    private readonly IDisposable _allListeners;
    private readonly SqlClientCaptureObserver _observer;
    private bool _disposed;

    private SqlClientCapture(SqlServerCaptureOptions options)
    {
        _observer = new SqlClientCaptureObserver(options);
        _allListeners = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(this));
    }

    /// <summary>
    /// Starts capture, or returns the capture already running. Enabling twice does not double
    /// count: the second call returns the same instance.
    /// </summary>
    public static SqlClientCapture Enable(SqlServerCaptureOptions? options = null)
    {
        lock (EnableGate)
        {
            return _current ??= new SqlClientCapture(options ?? new SqlServerCaptureOptions());
        }
    }

    /// <summary>The capture currently running, if any.</summary>
    public static SqlClientCapture? Current
    {
        get
        {
            lock (EnableGate)
            {
                return _current;
            }
        }
    }

    public void Dispose()
    {
        lock (EnableGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }

        _allListeners.Dispose();

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Attach(DiagnosticListener listener)
    {
        if (_disposed || !string.Equals(listener.Name, ListenerName, StringComparison.Ordinal))
        {
            return;
        }

        _subscriptions.Add(listener.Subscribe(_observer));
    }

    private sealed class ListenerObserver(SqlClientCapture owner) : IObserver<DiagnosticListener>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DiagnosticListener value) => owner.Attach(value);
    }
}
