using QueryWatt.Core.Instrumentation;

namespace QueryWatt;

/// <summary>
/// The QueryWatt entry point. Named <c>Watt</c> rather than <c>QueryWatt</c> because a type cannot
/// share a name with the <c>QueryWatt.*</c> namespaces — the name would bind to the namespace.
/// Contract v2 §15.
/// </summary>
/// <example>
/// <code>
/// using var m = Watt.Measure("orders.pending-by-customer");
/// // existing query, unchanged
/// m.Complete();
/// </code>
/// </example>
public static class Watt
{
    private static IMeasurementSink _sink = NullMeasurementSink.Instance;

    /// <summary>
    /// How much measurement is permitted. <see cref="InstrumentationLevel.Off"/> by default, so an
    /// application that ships the measurement blocks pays nothing until it opts in.
    /// </summary>
    public static InstrumentationLevel Instrumentation { get; set; } = InstrumentationLevel.Off;

    /// <summary>
    /// Convenience over <see cref="Instrumentation"/>. Setting it true selects
    /// <see cref="InstrumentationLevel.Full"/> when currently off, and keeps an explicitly chosen
    /// level otherwise; setting it false selects <see cref="InstrumentationLevel.Off"/>.
    /// </summary>
    public static bool Enabled
    {
        get => Instrumentation != InstrumentationLevel.Off;
        set => Instrumentation = value
            ? Instrumentation == InstrumentationLevel.Off
                ? InstrumentationLevel.Full
                : Instrumentation
            : InstrumentationLevel.Off;
    }

    /// <summary>Where finished scopes are published. Discards everything by default.</summary>
    public static IMeasurementSink Sink
    {
        get => _sink;
        set => _sink = value ?? NullMeasurementSink.Instance;
    }

    /// <summary>The innermost open scope on this execution context, or null.</summary>
    public static MeasurementScopeState? Current => MeasurementContext.Current;

    /// <summary>
    /// Opens a measurement scope around the caller's existing query. When measurement is off this
    /// returns a no-op struct: no allocation, no ambient state, no server interaction.
    /// </summary>
    /// <param name="queryId">
    /// The stable identity of this operation, owned by the developer. Validated only when
    /// measurement is enabled, which is development and test.
    /// </param>
    public static MeasurementScope Measure(string queryId)
    {
        if (Instrumentation == InstrumentationLevel.Off)
        {
            return default;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);

        var parent = MeasurementContext.Current;
        var depth = parent is null ? 0 : parent.Depth + 1;

        if (depth >= MeasurementContext.MaxDepth && parent is not null)
        {
            parent.AddDiagnostic(
                DiagnosticCode.NestingDepthExceeded,
                $"Nesting depth {MeasurementContext.MaxDepth} reached at '{queryId}'. Its commands "
                + $"are attributed to '{parent.QueryId}'.");

            return new MeasurementScope(parent, owns: false);
        }

        var state = new MeasurementScopeState(queryId, parent, depth, Instrumentation, _sink);
        MeasurementContext.Enter(state);
        return new MeasurementScope(state, owns: true);
    }

    /// <summary>Restores the defaults. Intended for tests, which share this static state.</summary>
    public static void Reset()
    {
        Instrumentation = InstrumentationLevel.Off;
        _sink = NullMeasurementSink.Instance;
    }
}

/// <summary>
/// A fully-spelled alias over <see cref="Watt"/>, for codebases that prefer it at the call site.
/// </summary>
public static class QueryWattScope
{
    /// <inheritdoc cref="Watt.Measure"/>
    public static MeasurementScope Begin(string queryId) => Watt.Measure(queryId);
}
