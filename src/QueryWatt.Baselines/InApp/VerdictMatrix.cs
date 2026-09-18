using System.Globalization;

namespace QueryWatt.Baselines.InApp;

/// <summary>What a run's measurement means next to its baseline. Contract v2 §10.</summary>
public enum InAppVerdict
{
    /// <summary>Within threshold on every gating metric.</summary>
    Unchanged,

    /// <summary>Measurably cheaper per row than the baseline.</summary>
    Improved,

    /// <summary>More work per row than the baseline allows. This is the one that fails a build.</summary>
    Regression,

    /// <summary>More work per row, and the plan changed too, which usually names the cause.</summary>
    PlanRegression,

    /// <summary>The plan changed but the cost did not. Worth a look before it becomes a surprise.</summary>
    Suspect,

    /// <summary>Raw reads moved with the row count, so the data grew rather than the query worsening.</summary>
    DataChange,

    /// <summary>This scenario has never been accepted, so there is nothing to compare it with.</summary>
    NewScenario,

    /// <summary>Something differs that would make the comparison dishonest.</summary>
    Incomparable
}

/// <summary>One scenario's verdict, with the reason spelled out.</summary>
public sealed record InAppVerdictResult(
    string QueryId,
    string ScenarioKey,
    string Source,
    InAppVerdict Verdict,
    string Reason)
{
    /// <summary>The accepted numbers, when this scenario had a baseline.</summary>
    public InAppMetrics? Baseline { get; init; }

    /// <summary>The numbers measured this run.</summary>
    public InAppMetrics? Current { get; init; }

    /// <summary>Change in reads per row, as a fraction; null when there is nothing to compare.</summary>
    public double? ReadsPerRowDelta { get; init; }

    /// <summary>True for the verdicts that should fail a build.</summary>
    public bool Fails => Verdict is InAppVerdict.Regression or InAppVerdict.PlanRegression;
}

/// <summary>The whole run's verdict.</summary>
public sealed record InAppVerificationResult(
    InAppVerdict Verdict,
    int ExitCode,
    IReadOnlyList<InAppVerdictResult> Results)
{
    /// <summary>The scenarios that failed.</summary>
    public IReadOnlyList<InAppVerdictResult> Failures =>
        Results.Where(result => result.Fails).ToArray();
}

/// <summary>
/// Decides what a measurement means next to its baseline.
/// </summary>
/// <remarks>
/// The matrix exists because the naive comparison — raw logical reads, up or down — is wrong in the
/// two cases that matter most. A table that grew overnight raises reads without anybody writing a
/// worse query, and a query called with a different set of filters is a different query. So the
/// gating metric is reads <em>per row</em>, a scenario is identified by its parameter shape, and a
/// scenario nobody has accepted yet is reported, never failed.
/// </remarks>
public static class VerdictMatrix
{
    /// <summary>Compares one run's samples with a baseline.</summary>
    /// <param name="baseline">The accepted baseline, or null when there is no file yet.</param>
    /// <param name="samples">The samples measured this run.</param>
    /// <param name="thresholds">Thresholds to use when an entry names none.</param>
    /// <returns>Per-scenario verdicts and the run's overall verdict.</returns>
    public static InAppVerificationResult Compare(
        InAppBaselineDocument? baseline,
        IEnumerable<InAppSample> samples,
        InAppThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var defaults = thresholds ?? InAppThresholds.Default;

        var results = samples
            .Select(sample => Compare(baseline?.Find(sample.QueryId, sample.ScenarioKey, sample.Source), sample, defaults))
            .OrderBy(result => result.QueryId, StringComparer.Ordinal)
            .ThenBy(result => result.ScenarioKey, StringComparer.Ordinal)
            .ToArray();

        var worst = results.Length == 0
            ? InAppVerdict.Unchanged
            : results.Select(result => result.Verdict).OrderByDescending(Severity).First();

        return new InAppVerificationResult(worst, worst is InAppVerdict.Regression or InAppVerdict.PlanRegression ? 1 : 0, results);
    }

    /// <summary>Compares one scenario.</summary>
    /// <param name="entry">The accepted entry, or null when this scenario is new.</param>
    /// <param name="sample">The sample measured this run.</param>
    /// <param name="defaults">Thresholds to use when the entry names none.</param>
    /// <returns>The verdict.</returns>
    public static InAppVerdictResult Compare(
        InAppBaselineEntry? entry,
        InAppSample sample,
        InAppThresholds defaults)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(defaults);

        if (entry is null)
        {
            return Result(
                sample,
                InAppVerdict.NewScenario,
                "This scenario has not been accepted yet, so there is nothing to compare it with. "
                + "Accept it once you are happy with these numbers.");
        }

        if (Differs(entry.EnvironmentFingerprint, sample.EnvironmentFingerprint))
        {
            return Result(
                sample,
                InAppVerdict.Incomparable,
                "The server differs from the one the baseline was accepted on, so a difference in "
                + "reads would say nothing about the query.",
                entry);
        }

        if (Differs(entry.DatasetFingerprint, sample.DatasetFingerprint))
        {
            return Result(
                sample,
                InAppVerdict.Incomparable,
                "The data differs from the data the baseline was accepted against, so a difference "
                + "in reads would say nothing about the query.",
                entry);
        }

        var thresholds = entry.Thresholds ?? defaults;
        var baselineMetrics = entry.Metrics;
        var current = sample.Metrics;

        var planChanged = Differs(entry.PlanFingerprint, sample.PlanFingerprint);
        var readsPerRowDelta = Delta(baselineMetrics.ReadsPerRow, current.ReadsPerRow);

        // Below the floor, ratios are noise: two reads becoming three is not a regression, and
        // reporting it as one teaches developers to stop reading the output.
        var tooSmallToJudge = baselineMetrics.LogicalReads < thresholds.MinimumLogicalReads
                              && current.LogicalReads < thresholds.MinimumLogicalReads;

        if (tooSmallToJudge)
        {
            return Result(
                sample,
                planChanged ? InAppVerdict.Suspect : InAppVerdict.Unchanged,
                planChanged
                    ? "The plan changed, but this query is too small for its cost to say anything."
                    : $"Both runs stay under {thresholds.MinimumLogicalReads} logical reads.",
                entry,
                readsPerRowDelta);
        }

        if (readsPerRowDelta > thresholds.ReadsPerRowIncreaseRatio)
        {
            var verdict = planChanged ? InAppVerdict.PlanRegression : InAppVerdict.Regression;

            return Result(
                sample,
                verdict,
                $"Reads per row rose {Percent(readsPerRowDelta)} "
                + $"({baselineMetrics.ReadsPerRow} → {current.ReadsPerRow}), past the "
                + $"{Percent(thresholds.ReadsPerRowIncreaseRatio)} allowed"
                + (planChanged ? ", and the plan changed." : "."),
                entry,
                readsPerRowDelta);
        }

        if (readsPerRowDelta < -thresholds.ReadsPerRowIncreaseRatio)
        {
            return Result(
                sample,
                InAppVerdict.Improved,
                $"Reads per row fell {Percent(-readsPerRowDelta)} "
                + $"({baselineMetrics.ReadsPerRow} → {current.ReadsPerRow}).",
                entry,
                readsPerRowDelta);
        }

        var rowsDelta = Delta(baselineMetrics.RowsReturned, current.RowsReturned);
        var readsDelta = Delta(baselineMetrics.LogicalReads, current.LogicalReads);

        if (readsDelta > thresholds.ReadsPerRowIncreaseRatio && rowsDelta > 0d)
        {
            return Result(
                sample,
                InAppVerdict.DataChange,
                $"Total reads rose {Percent(readsDelta)}, but rows rose {Percent(rowsDelta)} and the "
                + "cost per row held. The data grew; the query did not get worse.",
                entry,
                readsPerRowDelta);
        }

        if (planChanged)
        {
            return Result(
                sample,
                InAppVerdict.Suspect,
                "The plan changed while the cost per row held. Worth a look before it becomes a "
                + "surprise under different data.",
                entry,
                readsPerRowDelta);
        }

        var cpuDelta = Delta(baselineMetrics.CpuTimeMilliseconds, current.CpuTimeMilliseconds);
        if (cpuDelta > thresholds.CpuIncreaseRatio && current.CpuTimeMilliseconds > 0L)
        {
            return Result(
                sample,
                InAppVerdict.Suspect,
                $"CPU time rose {Percent(cpuDelta)} while reads per row held.",
                entry,
                readsPerRowDelta);
        }

        return Result(
            sample,
            InAppVerdict.Unchanged,
            $"Reads per row held at {current.ReadsPerRow}.",
            entry,
            readsPerRowDelta);
    }

    private static InAppVerdictResult Result(
        InAppSample sample,
        InAppVerdict verdict,
        string reason,
        InAppBaselineEntry? entry = null,
        double? readsPerRowDelta = null) =>
        new(sample.QueryId, sample.ScenarioKey, sample.Source, verdict, reason)
        {
            Baseline = entry?.Metrics,
            Current = sample.Metrics,
            ReadsPerRowDelta = readsPerRowDelta
        };

    private static bool Differs(string? baseline, string? current) =>
        !string.IsNullOrEmpty(baseline)
        && !string.IsNullOrEmpty(current)
        && !string.Equals(baseline, current, StringComparison.Ordinal);

    private static double Delta(double baseline, double current) =>
        baseline <= 0d ? (current > 0d ? 1d : 0d) : (current - baseline) / baseline;

    private static string Percent(double ratio) =>
        (ratio * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static int Severity(InAppVerdict verdict) => verdict switch
    {
        InAppVerdict.PlanRegression => 6,
        InAppVerdict.Regression => 5,
        InAppVerdict.Incomparable => 4,
        InAppVerdict.Suspect => 3,
        InAppVerdict.DataChange => 2,
        InAppVerdict.NewScenario => 1,
        InAppVerdict.Improved => 0,
        _ => 0
    };
}
