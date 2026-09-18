using QueryWatt.Core.Instrumentation;

namespace QueryWatt.SqlServer.Capture;

/// <summary>Knobs for in-app capture. Contract v2 §6, §12.</summary>
public sealed class SqlServerCaptureOptions
{
    /// <summary>
    /// When true (the default) QueryWatt applies <c>SET STATISTICS IO/TIME</c> to the
    /// application's connection so reads and CPU can be observed. Ignored unless the level is
    /// <see cref="InstrumentationLevel.Full"/>.
    /// </summary>
    public bool EnableConnectionInstrumentation { get; init; } = true;

    /// <summary>
    /// Commands whose text is longer than this are stored truncated. Identity stays intact
    /// because the normalized hash is taken before truncation.
    /// </summary>
    public int MaxCommandTextLength { get; init; } = 8_000;

    /// <summary>
    /// Keep the raw <c>STATISTICS</c> messages on each command. Useful while validating the
    /// parser against SSMS; noisy afterwards.
    /// </summary>
    public bool KeepRawMessages { get; init; }

    /// <summary>
    /// The source recorded for captured commands. The Dapper, EF Core and NHibernate packages
    /// re-tag their own commands; plain ADO.NET keeps this value.
    /// </summary>
    public MeasurementSource DefaultSource { get; init; } = MeasurementSource.AdoNet;
}
