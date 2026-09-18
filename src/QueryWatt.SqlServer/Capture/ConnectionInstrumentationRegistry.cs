using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Tracks one <see cref="ConnectionInstrumentation"/> per connection without keeping the
/// connection alive.
/// </summary>
internal sealed class ConnectionInstrumentationRegistry
{
    private readonly ConditionalWeakTable<SqlConnection, ConnectionInstrumentation> _entries = new();

    public ConnectionInstrumentation? For(object? connection)
    {
        if (connection is not SqlConnection sqlConnection)
        {
            return null;
        }

        return _entries.GetValue(sqlConnection, static key => new ConnectionInstrumentation(key));
    }
}
