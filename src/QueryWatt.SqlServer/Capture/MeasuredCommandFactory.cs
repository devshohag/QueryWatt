using System.Collections;
using System.Data;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.SqlServer.Capture;

/// <summary>Client-side counters taken from <c>SqlConnection.RetrieveStatistics()</c>.</summary>
public sealed record ClientCommandStatistics(
    long? RowsReturned,
    long? ServerRoundtrips,
    long? BytesReceived)
{
    public static readonly ClientCommandStatistics None = new(null, null, null);

    /// <summary>
    /// Reads the three counters QueryWatt uses. Keys absent from the dictionary stay null rather
    /// than defaulting to zero.
    /// </summary>
    public static ClientCommandStatistics From(IDictionary? statistics)
    {
        if (statistics is null)
        {
            return None;
        }

        return new ClientCommandStatistics(
            Read(statistics, "SelectRows"),
            Read(statistics, "ServerRoundtrips"),
            Read(statistics, "BytesReceived"));
    }

    private static long? Read(IDictionary statistics, string key)
    {
        if (!statistics.Contains(key))
        {
            return null;
        }

        return statistics[key] switch
        {
            long value => value,
            int value => value,
            _ => null
        };
    }
}

/// <summary>
/// Assembles a <see cref="MeasuredCommand"/> from the pieces the capture layer gathered. Every
/// metric that was not acquired stays null. Contract v2 §5.
/// </summary>
public static class MeasuredCommandFactory
{
    public static MeasuredCommand Create(
        int ordinal,
        string? commandText,
        CommandType commandType,
        MeasurementSource source,
        IReadOnlyList<CommandParameterInfo> parameters,
        double? clientDurationMilliseconds,
        Exception? exception,
        ParsedStatistics? serverStatistics,
        ClientCommandStatistics clientStatistics,
        int maxCommandTextLength = 8_000,
        IReadOnlyList<string>? rawMessages = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var text = commandText ?? string.Empty;
        if (maxCommandTextLength > 0 && text.Length > maxCommandTextLength)
        {
            text = text[..maxCommandTextLength];
        }

        var command = new MeasuredCommand(
            ordinal,
            text,
            commandType,
            source,
            parameters,
            exception is null ? CommandOutcome.Succeeded : CommandOutcome.Failed)
        {
            ClientDurationMilliseconds = clientDurationMilliseconds,
            RowsReturned = clientStatistics.RowsReturned,
            ServerRoundtrips = clientStatistics.ServerRoundtrips,
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message,
            SqlErrorNumber = SqlErrorNumber(exception),
            RawMessages = rawMessages ?? Array.Empty<string>()
        };

        if (serverStatistics is null)
        {
            return command;
        }

        return command with
        {
            LogicalReads = serverStatistics.LogicalReads,
            LobLogicalReads = serverStatistics.LobLogicalReads,
            PhysicalReads = serverStatistics.PhysicalReads,
            ReadAheadReads = serverStatistics.ReadAheadReads,
            LobPhysicalReads = serverStatistics.LobPhysicalReads,
            LobReadAheadReads = serverStatistics.LobReadAheadReads,
            CpuTimeMilliseconds = serverStatistics.CpuTimeMilliseconds,
            ServerElapsedTimeMilliseconds = serverStatistics.Statements
                .Sum(statement => statement.ServerElapsedTimeMilliseconds)
        };
    }

    private static int? SqlErrorNumber(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var property = exception.GetType().GetProperty("Number");
        if (property is null)
        {
            return null;
        }

        try
        {
            return property.GetValue(exception) as int?;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
