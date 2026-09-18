namespace QueryWatt.Core.Instrumentation;

/// <summary>How much QueryWatt is allowed to do while the application runs. Contract v2 §12.</summary>
public enum InstrumentationLevel
{
    /// <summary>No measurement at all. The default. A scope is a no-op struct.</summary>
    Off = 0,

    /// <summary>
    /// Client-side only: command identity, duration, rows, round trips, call counts. No session
    /// <c>SET</c> option is touched, so reads, CPU and plan stay null. Never baselined.
    /// </summary>
    Light = 1,

    /// <summary>
    /// Full measurement: connection instrumentation for <c>STATISTICS IO/TIME</c> plus plan
    /// capture on a separate connection. Development, test and CI only.
    /// </summary>
    Full = 2
}

/// <summary>Terminal state of a measurement scope. Contract v2 §3.</summary>
public enum MeasurementStatus
{
    /// <summary>The scope is still open.</summary>
    Running = 0,

    /// <summary><c>Complete()</c> was called and every started command finished.</summary>
    Completed = 1,

    /// <summary><c>Fail(exception)</c> was called, or an exception escaped the scope.</summary>
    Failed = 2,

    /// <summary>The operation was cancelled.</summary>
    Cancelled = 3,

    /// <summary>
    /// Disposed without a terminal call, or a command started and never finished.
    /// Reported, never written to a baseline.
    /// </summary>
    Abandoned = 4
}

/// <summary>Which access stack produced a command. Part of baseline identity. Contract v2 §1.1.</summary>
public enum MeasurementSource
{
    Unknown = 0,
    AdoNet = 1,
    Dapper = 2,
    EfCore = 3,
    NHibernate = 4,

    /// <summary>The v1 CLI runner. Never compared against the in-app sources.</summary>
    CliSql = 5
}

/// <summary>Whether a command finished, faulted, or was never observed finishing.</summary>
public enum CommandOutcome
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>Stable diagnostic codes. Contract v2 §16.</summary>
public enum DiagnosticCode
{
    NoCommandsInScope = 1,
    OpenReaderAtComplete = 2,
    AbandonedScope = 3,
    DuplicateQueryId = 4,
    NonEnglishSessionLanguage = 5,
    MarsAttributionUnsafe = 6,
    InlineLiteralsDetected = 7,
    PlanCaptureSkipped = 8,
    LowConfidenceSample = 9,
    NestingDepthExceeded = 10
}
