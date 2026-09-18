using System.Data;
using System.Data.Common;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Turns a command's parameter collection into metadata. Reads the name, type, size, precision,
/// scale and <em>whether the value was null</em> — never the value itself. Contract v2 §2.2, §13.
/// </summary>
public static class SqlParameterDescriber
{
    public static IReadOnlyList<CommandParameterInfo> Describe(DbCommand? command)
    {
        if (command is null)
        {
            return Array.Empty<CommandParameterInfo>();
        }

        DbParameterCollection parameters;
        try
        {
            parameters = command.Parameters;
        }
        catch (Exception)
        {
            return Array.Empty<CommandParameterInfo>();
        }

        if (parameters.Count == 0)
        {
            return Array.Empty<CommandParameterInfo>();
        }

        var described = new List<CommandParameterInfo>(parameters.Count);

        foreach (DbParameter parameter in parameters)
        {
            described.Add(DescribeParameter(parameter));
        }

        return described;
    }

    public static CommandParameterInfo DescribeParameter(DbParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return new CommandParameterInfo(
            parameter.ParameterName ?? string.Empty,
            SafeDbType(parameter),
            IsNull(parameter),
            Positive(SafeSize(parameter)),
            Positive(SafePrecision(parameter)),
            Positive(SafeScale(parameter)));
    }

    private static bool IsNull(DbParameter parameter)
    {
        try
        {
            // Reading null-ness is not reading the value: the object is never retained,
            // inspected, logged or hashed.
            return parameter.Value is null or DBNull;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DbType SafeDbType(DbParameter parameter)
    {
        try
        {
            return parameter.DbType;
        }
        catch (Exception)
        {
            // Some providers throw when the type was never inferred. Identity beats a crash
            // inside measurement code.
            return DbType.Object;
        }
    }

    private static int SafeSize(DbParameter parameter)
    {
        try
        {
            return parameter.Size;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static byte SafePrecision(DbParameter parameter)
    {
        try
        {
            return parameter.Precision;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static byte SafeScale(DbParameter parameter)
    {
        try
        {
            return parameter.Scale;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int? Positive(int value) => value > 0 ? value : null;

    private static byte? Positive(byte value) => value > 0 ? value : null;
}
