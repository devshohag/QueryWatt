using QueryWatt.Baselines;
using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using QueryWatt.SqlServer.Capture;

namespace QueryWatt.Testing;

/// <summary>
/// Turns a measurement into an ordinary failing test. This is how a team without pull requests or
/// CI still gets a build that stops when a query gets worse.
/// </summary>
/// <remarks>
/// <para>
/// The guard owns the instrumentation for the length of one test: it switches measurement on, sends
/// records to its own sink, and puts everything back on dispose, so tests that do not measure are
/// unaffected and tests that do are not affected by each other.
/// </para>
/// <example>
/// <code>
/// [Fact]
/// public void OrderSearchStaysEfficient()
/// {
///     using var guard = QueryWattGuard.Start();
///
///     guard.Measure("orders.search", () => _repository.Search(customerId, placedAfter));
///
///     guard.AssertNoRegression();
/// }
/// </code>
/// </example>
/// <para>
/// Running with <c>QUERYWATT_ACCEPT=1</c> writes what was measured into the baseline instead of
/// judging it — the same gesture as updating a snapshot, and the only way the numbers ever change.
/// </para>
/// </remarks>
public sealed class QueryWattGuard : IDisposable
{
    private readonly QueryWattGuardOptions _options;
    private readonly InMemoryMeasurementSink _sink = new();
    private readonly InstrumentationLevel _previousLevel;
    private readonly IMeasurementSink _previousSink;
    private readonly IDisposable? _capture;

    private bool _disposed;

    private QueryWattGuard(QueryWattGuardOptions options)
    {
        _options = options;
        _previousLevel = Watt.Instrumentation;
        _previousSink = Watt.Sink;

        _capture = options.EnableCapture ? SqlClientCapture.Enable() : null;

        Watt.Sink = _sink;
        Watt.Instrumentation = options.Level;
    }

    /// <summary>Starts a guard for one test.</summary>
    /// <param name="options">Options, or null for the defaults.</param>
    /// <returns>The guard, which must be disposed at the end of the test.</returns>
    public static QueryWattGuard Start(QueryWattGuardOptions? options = null) =>
        new(options ?? new QueryWattGuardOptions());

    /// <summary>True when this run was asked to accept rather than judge.</summary>
    public bool Accepting =>
        _options.Accept
        || Environment.GetEnvironmentVariable("QUERYWATT_ACCEPT") is "1" or "true" or "TRUE";

    /// <summary>Everything measured so far in this test.</summary>
    public IReadOnlyList<MeasurementRecord> Records => _sink.RootRecords;

    /// <summary>Measures one operation. The operation's own result is returned untouched.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="queryId">The name this work is tracked under.</param>
    /// <param name="operation">The application code to measure.</param>
    /// <returns>Whatever the operation returned.</returns>
    public T Measure<T>(string queryId, Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var scope = Watt.Measure(queryId);

        try
        {
            var result = operation();
            scope.Complete();
            return result;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    /// <summary>Measures one operation that returns nothing.</summary>
    /// <param name="queryId">The name this work is tracked under.</param>
    /// <param name="operation">The application code to measure.</param>
    public void Measure(string queryId, Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Measure(queryId, () =>
        {
            operation();
            return true;
        });
    }

    /// <inheritdoc cref="Measure{T}(string, Func{T})"/>
    public async Task<T> MeasureAsync<T>(string queryId, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var scope = Watt.Measure(queryId);

        try
        {
            var result = await operation().ConfigureAwait(false);
            scope.Complete();
            return result;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    /// <inheritdoc cref="Measure(string, Action)"/>
    public async Task MeasureAsync(string queryId, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await MeasureAsync(queryId, async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Compares what this test measured with the accepted baseline, and throws when a query got
    /// worse. A scenario nobody has accepted yet does not fail; it is reported so the developer can
    /// accept it deliberately.
    /// </summary>
    /// <param name="baselinePath">Baseline path, or null to find it near the repository root.</param>
    /// <param name="thresholds">Thresholds, or null for the defaults.</param>
    /// <exception cref="QueryWattRegressionException">A query regressed, or nothing was measurable.</exception>
    public void AssertNoRegression(string? baselinePath = null, InAppThresholds? thresholds = null)
    {
        var path = BaselineLocator.Resolve(baselinePath ?? _options.BaselinePath);
        var records = _sink.RootRecords;
        var samples = InAppBaselineFactory.Summarise(records);

        if (samples.Count == 0)
        {
            throw new QueryWattRegressionException(NothingMeasurable(records));
        }

        var existing = InAppBaselineStore.Read(path);

        if (Accepting)
        {
            InAppBaselineStore.Write(
                path,
                InAppBaselineFactory.Merge(existing, samples, BaselineContract.ToolVersion));

            _options.Log($"QueryWatt accepted {samples.Count} scenarios into {path}.");
            return;
        }

        var result = VerdictMatrix.Compare(existing, samples, thresholds ?? _options.Thresholds);

        if (result.Failures.Count > 0)
        {
            throw new QueryWattRegressionException(
                InAppVerificationRenderer.Render(result, ReportFormat.Console)
                + Environment.NewLine + Environment.NewLine
                + "If this is the cost you meant to pay, re-run with QUERYWATT_ACCEPT=1 to move the "
                + $"baseline at {path}.");
        }

        foreach (var scenario in result.Results.Where(item => item.Verdict != InAppVerdict.Unchanged))
        {
            _options.Log($"QueryWatt {scenario.Verdict}: {scenario.QueryId} — {scenario.Reason}");
        }
    }

    /// <summary>Writes what this test measured into the baseline, whatever the verdict would be.</summary>
    /// <param name="baselinePath">Baseline path, or null to find it near the repository root.</param>
    public void Accept(string? baselinePath = null)
    {
        var path = BaselineLocator.Resolve(baselinePath ?? _options.BaselinePath);
        var samples = InAppBaselineFactory.Summarise(_sink.RootRecords);

        InAppBaselineStore.Write(
            path,
            InAppBaselineFactory.Merge(
                InAppBaselineStore.Read(path),
                samples,
                BaselineContract.ToolVersion));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Watt.Instrumentation = _previousLevel;
        Watt.Sink = _previousSink;

        if (_options.DisposeCapture)
        {
            _capture?.Dispose();
        }
    }

    /// <summary>
    /// Explains why a run that clearly did something produced nothing to judge. Every one of these
    /// has bitten somebody, so each gets named rather than left as "no data".
    /// </summary>
    private static string NothingMeasurable(IReadOnlyList<MeasurementRecord> records)
    {
        if (records.Count == 0)
        {
            return "QueryWatt measured nothing. Wrap the work in guard.Measure(\"...\", () => ...), "
                   + "and make sure the database calls happen inside it.";
        }

        var reasons = new List<string>();

        if (records.Any(record => record.Status != MeasurementStatus.Completed))
        {
            reasons.Add(
                "some scopes did not complete — a scope that failed or was abandoned is never "
                + "baselined");
        }

        if (records.Any(record => record.Commands.Count == 0))
        {
            reasons.Add("some scopes issued no database commands at all");
        }

        if (records.Any(record =>
                record.Commands.Count > 0
                && record.Commands.Any(command => command.LogicalReads is null)))
        {
            reasons.Add(
                "server-side reads are missing — measure at Full instrumentation, wrap the "
                + "connection with QueryWattConnection.Wrap so its readers finish before closing, "
                + "and close readers before the scope completes");
        }

        var diagnostics = records
            .SelectMany(record => record.Diagnostics)
            .Select(diagnostic => diagnostic.Code.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var detail = diagnostics.Length == 0
            ? string.Empty
            : $" Diagnostics raised: {string.Join(", ", diagnostics)}.";

        return $"QueryWatt has nothing it can judge: {string.Join("; ", reasons)}.{detail}";
    }
}
