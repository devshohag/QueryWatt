using System.Collections.Concurrent;
using System.Reflection;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Reads named members off a SqlClient diagnostic payload. The payload types are internal to the
/// driver and differ between Microsoft.Data.SqlClient and System.Data.SqlClient, so they are read
/// by name, with the accessor cached per type.
/// </summary>
internal static class PayloadReader
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> Properties =
        new();

    public static T? Read<T>(object? payload, string name)
        where T : class
    {
        if (payload is null)
        {
            return null;
        }

        var property = Properties.GetOrAdd(
            (payload.GetType(), name),
            key => key.Type.GetProperty(
                key.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

        if (property is null)
        {
            return null;
        }

        try
        {
            return property.GetValue(payload) as T;
        }
        catch (Exception)
        {
            // A payload that will not yield a member is a capture miss, never an application fault.
            return null;
        }
    }

    public static Guid ReadOperationId(object? payload)
    {
        if (payload is null)
        {
            return Guid.Empty;
        }

        var property = Properties.GetOrAdd(
            (payload.GetType(), "OperationId"),
            key => key.Type.GetProperty(
                key.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

        if (property is null)
        {
            return Guid.Empty;
        }

        try
        {
            return property.GetValue(payload) is Guid operationId ? operationId : Guid.Empty;
        }
        catch (Exception)
        {
            return Guid.Empty;
        }
    }
}
