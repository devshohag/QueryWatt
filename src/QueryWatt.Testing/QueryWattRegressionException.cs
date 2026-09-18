namespace QueryWatt.Testing;

/// <summary>
/// Thrown when a guarded test measured a query that got worse. Every test framework reports an
/// exception as a failure, so QueryWatt needs no adapter for xunit, NUnit or MSTest.
/// </summary>
/// <remarks>
/// The message is the report a developer needs to act: which query, which scenario, what the numbers
/// were and are, and how to accept the change if it was intended.
/// </remarks>
public sealed class QueryWattRegressionException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The rendered report.</param>
    public QueryWattRegressionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The rendered report.</param>
    /// <param name="innerException">The underlying failure.</param>
    public QueryWattRegressionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
