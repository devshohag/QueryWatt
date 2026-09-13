namespace QueryWatt.SqlServer;

public sealed class MeasurementParseException : Exception
{
    public MeasurementParseException(string message)
        : base(message)
    {
    }
}
