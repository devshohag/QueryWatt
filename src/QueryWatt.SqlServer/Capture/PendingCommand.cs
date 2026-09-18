using System.Collections;
using System.Data;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// A command observed inside a scope, held until the scope finishes. Nothing is recorded at
/// execution time because <c>STATISTICS</c> messages for a reader only arrive once the reader has
/// been drained, which happens after the driver's "after" event. Contract v2 §6.
/// </summary>
internal sealed class PendingCommand
{
    public required int Ordinal { get; init; }

    public required string CommandText { get; init; }

    public required CommandType CommandType { get; init; }

    public required IReadOnlyList<CommandParameterInfo> Parameters { get; init; }

    public required ConnectionInstrumentation? Instrumentation { get; init; }

    public required int MessageStart { get; init; }

    public required long StartTimestamp { get; init; }

    public double? DurationMilliseconds { get; set; }

    public Exception? Exception { get; set; }

    public IDictionary? ClientStatistics { get; set; }

    public bool Finished { get; set; }

    /// <summary>The in-flight correlation key, so an unfinished command can still be cleaned up.</summary>
    public object? CorrelationKey { get; set; }
}
