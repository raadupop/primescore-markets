using System.Globalization;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Contracts;

/// <summary>Queue an on-demand pull of a source adapter (the UI's "pull now"; not an API endpoint).</summary>
public sealed record RequestSourcePull(string Source) : ICommand<SourcePullAck>;

/// <param name="Queued">False when the source is disabled or unknown; <paramref name="Reason"/> says why.</param>
public sealed record SourcePullAck(bool Queued, string? Reason) : ICommandAck;

/// <param name="StoredOnly">
/// Report what the run records hold and nothing inferred from this process's live state: a
/// separate process (the CLI) cannot tell an interrupted run from one still running elsewhere.
/// </param>
public sealed record GetSourceStatus(bool StoredOnly = false) : IQuery<IReadOnlyList<SourceStatus>>;

/// <param name="Description">One line saying what the adapter reads and records.</param>
/// <param name="Schedule">The adapter's schedule in New York time (<see cref="SourceSchedule.Describe"/>).</param>
/// <param name="Flags">Operational findings of the last run (e.g. a cross-check disagreement); never ledger facts.</param>
/// <param name="Note">One-line summary of the last run.</param>
public sealed record SourceStatus(
    string Source,
    bool Enabled,
    string? DisabledReason,
    bool Running,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    bool LastRunPartial,
    DateTimeOffset? NextRunAt,
    SourceRunCounts? LastRun,
    IReadOnlyList<SeriesStatus> Series,
    string Description = "",
    string Schedule = "",
    IReadOnlyList<string>? Flags = null,
    string? Note = null);

/// <param name="Revised">Values that differed from what was already recorded for the same observation; the first recorded value is kept.</param>
public sealed record SourceRunCounts(int Accepted, int Duplicates, int Revised, int Missing, int Rejected);

/// <param name="Missing">Observations the provider reports without a value (FRED "."), which are skipped, never filled.</param>
/// <param name="Url">The provider's page or file for the series, when it has one.</param>
public sealed record SeriesStatus(
    string SeriesId,
    string Instrument,
    SourceCategory Category,
    bool MappingVerified,
    long Recorded,
    DateTimeOffset? LatestObservedAt,
    string Timing,
    string? SourceIdentifier = null,
    Uri? Url = null);

/// <summary>
/// One external data source run by the Ingestion module's scheduler. Other modules register
/// adapters through this contract (<c>services.AddSingleton&lt;ISourceAdapter, X&gt;()</c>) without
/// referencing the Ingestion implementation. Adapters are singletons, and so is every dependency
/// they hold; an adapter that needs scoped handlers opens its own <c>IServiceScopeFactory</c> scope.
/// <see cref="SourcePullResult.RecordedSignals"/> carries Ingestion signal ids only: an adapter in
/// another module records its own ledger kinds and returns an empty list.
/// </summary>
public interface ISourceAdapter
{
    /// <summary>Key of run records, pull requests and status rows, e.g. <c>Cboe</c>, <c>FRED</c>.</summary>
    string Name { get; }

    /// <summary>One line for the Sources page.</summary>
    string Description { get; }

    /// <summary>Null when the adapter may run; otherwise why not (disabled, missing key, invalid option).</summary>
    string? DisabledReason { get; }

    /// <summary>Pull once when the engine starts, after the startup pulls of adapters registered before it.</summary>
    bool RunOnStartup { get; }

    SourceSchedule Schedule { get; }

    /// <summary>The series the adapter records or reads, for status coverage.</summary>
    IReadOnlyList<SourceSeries> Series { get; }

    Task<SourcePullResult> PullAsync(CancellationToken cancellationToken);

    /// <summary>Removes secrets (an API key) from text that may be logged or stored.</summary>
    string Redact(string text) => text;
}

/// <summary>Which New York dates a run or window applies to.</summary>
public enum SourceDays
{
    EveryDay,

    /// <summary>Monday to Friday.</summary>
    Weekdays,

    /// <summary>NYSE trading days (<see cref="MarketTime.IsTradingDay"/>).</summary>
    TradingDays,
}

/// <summary>One run a day at a New York wall-clock time.</summary>
public sealed record DailyRun(TimeOnly NewYorkTime, SourceDays Days);

/// <summary>
/// Runs every <paramref name="Interval"/> from <paramref name="StartNewYork"/> until before
/// <paramref name="EndNewYork"/>. An end at or before the start closes the window on the next New
/// York date. Slots step in UTC from the window's UTC start, so a window spanning a clock change
/// is an hour longer or shorter, never skipped. The day filter applies to the start date.
/// </summary>
public sealed record PollingWindow(TimeOnly StartNewYork, TimeOnly EndNewYork, TimeSpan Interval, SourceDays Days);

/// <summary>
/// When an adapter runs, as data: wall-clock times in America/New_York converted to UTC per run
/// through <see cref="MarketTime.AtNewYork"/>, so daylight saving is right by construction. A wall
/// time that does not exist (spring forward) or occurs twice (fall back) resolves to the standard
/// offset, as <see cref="MarketTime.AtNewYork"/> does.
/// </summary>
public sealed record SourceSchedule(IReadOnlyList<DailyRun> DailyRuns, IReadOnlyList<PollingWindow> PollingWindows)
{
    public static SourceSchedule OnRequest { get; } = new([], []);

    /// <summary>The first run strictly after <paramref name="instant"/>; null when the schedule has no runs.</summary>
    /// <exception cref="InvalidOperationException">A polling window's interval is not positive.</exception>
    public DateTimeOffset? NextAfter(DateTimeOffset instant)
    {
        foreach (var window in PollingWindows)
        {
            if (window.Interval <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"Polling interval must be positive (was {window.Interval}).");
            }
        }

        DateTimeOffset? next = null;
        var today = MarketTime.NewYorkDate(instant);

        // Yesterday covers a window still open after midnight; eight days ahead covers any run of closures.
        for (var date = today.AddDays(-1); date <= today.AddDays(8); date = date.AddDays(1))
        {
            foreach (var run in DailyRuns.Where(run => Applies(run.Days, date)))
            {
                next = Earliest(next, MarketTime.AtNewYork(date, run.NewYorkTime), instant);
            }

            foreach (var window in PollingWindows.Where(window => Applies(window.Days, date)))
            {
                var start = MarketTime.AtNewYork(date, window.StartNewYork);
                var end = MarketTime.AtNewYork(window.EndNewYork <= window.StartNewYork ? date.AddDays(1) : date, window.EndNewYork);
                var slots = instant < start ? 0 : ((instant - start).Ticks / window.Interval.Ticks) + 1;
                var slot = start + TimeSpan.FromTicks(slots * window.Interval.Ticks);
                if (slot < end)
                {
                    next = Earliest(next, slot, instant);
                }
            }
        }

        return next;
    }

    /// <summary>E.g. "every 15 min from 18:00 to 08:00 next day, every day (New York time)".</summary>
    public string Describe()
    {
        var parts = DailyRuns.Select(run => $"daily at {Time(run.NewYorkTime)}, {Days(run.Days)}")
            .Concat(PollingWindows.Select(window =>
                $"every {Interval(window.Interval)} from {Time(window.StartNewYork)} to {Time(window.EndNewYork)}"
                + $"{(window.EndNewYork <= window.StartNewYork ? " next day" : "")}, {Days(window.Days)}"))
            .ToArray();
        return parts.Length == 0 ? "on request only" : string.Join("; ", parts) + " (New York time)";
    }

    private static bool Applies(SourceDays days, DateOnly date) => days switch
    {
        SourceDays.Weekdays => MarketTime.IsBusinessDay(date),
        SourceDays.TradingDays => MarketTime.IsTradingDay(date),
        _ => true,
    };

    private static DateTimeOffset? Earliest(DateTimeOffset? current, DateTimeOffset candidate, DateTimeOffset after) =>
        candidate <= after || (current is { } existing && existing <= candidate) ? current : candidate;

    private static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Days(SourceDays days) => days switch
    {
        SourceDays.Weekdays => "Monday to Friday",
        SourceDays.TradingDays => "NYSE trading days",
        _ => "every day",
    };

    private static string Interval(TimeSpan interval) =>
        interval.TotalMinutes >= 60 && interval.Ticks % TimeSpan.TicksPerHour == 0
            ? $"{interval.TotalHours.ToString(CultureInfo.InvariantCulture)} h"
            : $"{interval.TotalMinutes.ToString(CultureInfo.InvariantCulture)} min";
}

/// <param name="SeriesId">The provider's id (a FRED series id, a Cboe index symbol).</param>
/// <param name="SourceIdentifier">The <c>source_identifier</c> the adapter records under, e.g. <c>cboe:VIX</c>.</param>
/// <param name="Timing">When an observation is stamped and when the provider publishes it.</param>
public sealed record SourceSeries(
    string SeriesId,
    string SourceIdentifier,
    string Instrument,
    SourceCategory Category,
    bool MappingVerified,
    string Timing,
    Uri? Url);

/// <param name="RecordedSignals">Ingestion signal ids newly recorded; announced to classification off the adapter's loop.</param>
/// <param name="ItemErrors">Per-item detail of what could not be pulled (a series, a file); the others were.</param>
/// <param name="ItemsAttempted">Items the run tried; when every one failed the run failed.</param>
/// <param name="Flags">Operational findings (e.g. a cross-check disagreement), stored on the run record only.</param>
/// <param name="Note">One-line run summary.</param>
public sealed record SourcePullResult(
    SourceRunCounts Counts,
    IReadOnlyList<Guid> RecordedSignals,
    IReadOnlyList<string> ItemErrors,
    int ItemsAttempted,
    IReadOnlyList<string> Flags,
    string? Note)
{
    public bool NothingPulled => ItemsAttempted > 0 && ItemErrors.Count >= ItemsAttempted;
}

/// <summary>
/// Source-identifier prefixes reserved for the engine's adapters (design §5.1). API submissions
/// using one are rejected, so an adapter's rows are only the adapter's. Later steps append.
/// </summary>
public static class SourcePrefixes
{
    public const string ApiProvider = "api";

    public static IReadOnlyList<string> Reserved { get; } =
        Array.AsReadOnly(new[] { "fred:", "cboe:", "cboe-vx:", "bls:", "bea:", "cal:", "nowcast:", "pm:", "gdelt:", "gpr:", "usgs:" });

    /// <summary>The reserved prefix <paramref name="sourceIdentifier"/> starts with (any letter case), or null.</summary>
    public static string? ReservedPrefixOf(string sourceIdentifier)
    {
        ArgumentNullException.ThrowIfNull(sourceIdentifier);
        return Reserved.FirstOrDefault(prefix => sourceIdentifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Recorded by an adapter: a reserved prefix and a provider other than <c>api</c>. API rows
    /// recorded before their prefix was reserved (e.g. <c>bls:CPI_YOY</c>) keep API treatment.
    /// </summary>
    public static bool IsAdapterRecorded(string sourceIdentifier, string provider) =>
        ReservedPrefixOf(sourceIdentifier) is not null && !string.Equals(provider, ApiProvider, StringComparison.OrdinalIgnoreCase);
}
