using System.Data.Common;

namespace QueryWatt.Core.Instrumentation;

/// <summary>
/// The developer-facing measurement block. A struct on purpose: when measurement is disabled this
/// is <c>default</c> and every member is a no-op, so the block costs nothing in production.
/// Contract v2 §3, §12, §15.
/// </summary>
public readonly struct MeasurementScope : IDisposable
{
    private readonly MeasurementScopeState? _state;
    private readonly bool _owns;

    internal MeasurementScope(MeasurementScopeState state, bool owns)
    {
        _state = state;
        _owns = owns;
    }

    /// <summary>False when measurement is off, or when this block attached to an outer scope.</summary>
    public bool IsEnabled => _state is not null;

    /// <summary>The live scope, for provider adapters. Null when measurement is off.</summary>
    public MeasurementScopeState? State => _state;

    /// <summary>The finished record, available after a terminal call. Null when off.</summary>
    public MeasurementRecord? Record => _state?.Record;

    /// <summary>Marks the measured operation as finished. Idempotent.</summary>
    public void Complete()
    {
        if (_owns)
        {
            _state?.Complete(null);
        }
    }

    /// <summary>
    /// Marks the operation as finished and supplies the row count, for shapes where no adapter
    /// can see it. Idempotent.
    /// </summary>
    public void Complete(long rowsReturned)
    {
        if (_owns)
        {
            _state?.Complete(rowsReturned);
        }
    }

    /// <summary>
    /// Marks the operation as faulted. Never throws — a measurement fault must not change what
    /// the application does. A cancellation is recorded as <see cref="MeasurementStatus.Cancelled"/>.
    /// </summary>
    public void Fail(Exception exception)
    {
        if (!_owns || _state is null)
        {
            return;
        }

        try
        {
            _state.Fail(exception ?? new InvalidOperationException("Failure reported without an exception."));
        }
        catch
        {
            // Swallowed deliberately.
        }
    }

    /// <summary>Names this scenario explicitly instead of deriving it from the parameter shape.</summary>
    public MeasurementScope Scenario(string name)
    {
        if (_owns && _state is not null && !string.IsNullOrWhiteSpace(name))
        {
            _state.SetScenario(name);
        }

        return this;
    }

    /// <summary>Explicit attribution fallback. Contract v2 §2.1.</summary>
    public void Attach(DbCommand command)
    {
        if (command is not null)
        {
            _state?.AttachCommand(command);
        }
    }

    /// <summary>
    /// A fallback, not a second way to complete: a scope disposed without <see cref="Complete()"/>
    /// or <see cref="Fail"/> becomes <see cref="MeasurementStatus.Abandoned"/> and never enters a
    /// baseline.
    /// </summary>
    public void Dispose()
    {
        if (_state is null)
        {
            return;
        }

        if (!_owns)
        {
            return;
        }

        if (!_state.IsTerminal)
        {
            _state.Abandon();
        }

        MeasurementContext.Exit(_state);
    }
}
