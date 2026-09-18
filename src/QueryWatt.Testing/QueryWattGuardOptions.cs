using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.Testing;

/// <summary>How a <see cref="QueryWattGuard"/> behaves for one test.</summary>
public sealed class QueryWattGuardOptions
{
    /// <summary>
    /// How much to measure. Full is the only level that produces server-side reads, and therefore
    /// the only level a regression can be judged from.
    /// </summary>
    public InstrumentationLevel Level { get; init; } = InstrumentationLevel.Full;

    /// <summary>
    /// The baseline file. A relative name is looked for near the repository root rather than in the
    /// test binaries, because that is where a committed baseline belongs.
    /// </summary>
    public string BaselinePath { get; init; } = InAppBaselineContract.DefaultFileName;

    /// <summary>Thresholds for this test, when the defaults do not suit it.</summary>
    public InAppThresholds? Thresholds { get; init; }

    /// <summary>Accept rather than judge, without setting the environment variable.</summary>
    public bool Accept { get; init; }

    /// <summary>
    /// Subscribe to SqlClient's diagnostics when the guard starts. Leave this on unless the
    /// application already enabled capture for itself.
    /// </summary>
    public bool EnableCapture { get; init; } = true;

    /// <summary>
    /// Unsubscribe when the guard is disposed. Off by default: capture is a process-wide singleton
    /// and other tests in the same run are usually still using it.
    /// </summary>
    public bool DisposeCapture { get; init; }

    /// <summary>
    /// Where the guard writes the lines a developer needs to read — new scenarios, accepted
    /// baselines. Defaults to the console, which every test runner shows on failure.
    /// </summary>
    public Action<string> Log { get; init; } = Console.WriteLine;
}
