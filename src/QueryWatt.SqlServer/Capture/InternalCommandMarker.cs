using System.Data.Common;
using System.Runtime.CompilerServices;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Marks the small number of commands QueryWatt issues on the application's own connection for its
/// own bookkeeping (the session-language probe, <c>SET STATISTICS IO/TIME ON</c>) so the capture
/// observer never attributes them to the caller's measurement scope. Contract v2 §6.
/// </summary>
internal static class InternalCommandMarker
{
    private static readonly ConditionalWeakTable<DbCommand, object> Marked = new();
    private static readonly object Token = new();

    public static void Mark(DbCommand command) => Marked.AddOrUpdate(command, Token);

    public static bool IsMarked(DbCommand? command) =>
        command is not null && Marked.TryGetValue(command, out _);
}