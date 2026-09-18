namespace QueryWatt.Core.Instrumentation;

/// <summary>
/// The ambient scope. A command executed inside a scope is attributed to the innermost open
/// scope; a command executed outside every scope is ignored entirely. Contract v2 §2, §4.
/// </summary>
public static class MeasurementContext
{
    /// <summary>
    /// Nesting cap. Beyond it, further scopes attach to the deepest accepted scope and a
    /// <see cref="DiagnosticCode.NestingDepthExceeded"/> diagnostic is raised. Contract v2 §4.
    /// </summary>
    public const int MaxDepth = 16;

    private static readonly AsyncLocal<MeasurementScopeState?> Ambient = new();

    /// <summary>The innermost open scope on this execution context, or null.</summary>
    public static MeasurementScopeState? Current => Ambient.Value;

    internal static void Enter(MeasurementScopeState state) => Ambient.Value = state;

    internal static void Exit(MeasurementScopeState state)
    {
        // Only unwind when the scope being closed is the one we are standing in. A scope closed
        // out of order must not resurrect an unrelated parent.
        if (ReferenceEquals(Ambient.Value, state))
        {
            Ambient.Value = state.Parent;
        }
    }
}
