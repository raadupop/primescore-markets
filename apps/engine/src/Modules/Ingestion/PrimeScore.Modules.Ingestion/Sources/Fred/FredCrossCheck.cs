using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Sources.Cboe;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Sources.Fred;

/// <summary>
/// FRED as a read-only cross-check (ADR-0009): FRED's terms forbid storing its content in a
/// database, so this adapter records nothing. For each configured Cboe index FRED also publishes,
/// it reads the last <c>CompareDays</c> closes recorded under <c>cboe:</c>, asks FRED for exactly
/// that date range, and flags a date where FRED differs beyond tolerance or reports a close the
/// <c>cboe:</c> series lacks. A flag names the index, the FRED id and the date, never FRED's value;
/// flags go on the run record (an operational finding), never the ledger. FRED's missing value (".")
/// is not compared. The FRED rows recorded before the demotion stay as recorded history.
/// </summary>
internal sealed partial class FredCrossCheck(
    FredClient client,
    IndicatorRegistry registry,
    IOptions<FredOptions> options,
    IOptions<CboeOptions> cboeOptions,
    IngestionReadStore reads,
    ILogger<FredCrossCheck> logger) : ISourceAdapter
{
    private const string HistoricalTiming = "recorded until 2026-09-29; no longer updated (ADR-0009)";

    public string Name => FredOptions.SourceName;

    public string Description => "Read-only cross-check of Cboe closes against FRED; records nothing (ADR-0009).";

    public string? DisabledReason => SourceOptions.TryRead(options, FredOptions.Section, out var invalid) is { } settings ? settings.DisabledReason() : invalid;

    public bool RunOnStartup => SourceOptions.TryRead(options, FredOptions.Section, out _)?.RunOnStartup ?? false;

    public SourceSchedule Schedule => SourceOptions.TryRead(options, FredOptions.Section, out _) is { } settings
        ? new([new DailyRun(settings.DailyRunNewYork, SourceDays.EveryDay)], [])
        : SourceSchedule.OnRequest;

    /// <summary>The series FRED supplied until the demotion, so the page still counts their recorded history.</summary>
    public IReadOnlyList<SourceSeries> Series => FredSeriesCatalog.Historical(registry)
        .Select(series => new SourceSeries(
            series.SeriesId, series.SourceIdentifier, series.Instrument, series.Category, series.Verified, HistoricalTiming, series.SourceUrl))
        .ToArray();

    public async Task<SourcePullResult> PullAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var pairs = FredSeriesCatalog.CrossCheckPairs(registry, cboeOptions.Value.SymbolList, settings.ExcludedSymbols);
        var flags = new List<string>();
        var errors = new List<string>();
        var (attempted, compared, seriesCompared) = (0, 0, 0);
        foreach (var pair in pairs)
        {
            var closes = await RecentCboeClosesAsync(pair, settings.CompareDays, cancellationToken).ConfigureAwait(false);
            if (closes.Count == 0)
            {
                continue;
            }

            attempted++;
            IReadOnlyList<FredObservation> observations;
            try
            {
                observations = await client.GetObservationsAsync(pair.FredSeriesId, closes.Keys.Min(), closes.Keys.Max(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One unavailable series (an unverified mapping, a timeout) must not block the others.
                errors.Add(client.Redact(exception is InvalidOperationException ? exception.Message : $"FRED {pair.FredSeriesId}: {exception.Message}"));
                continue;
            }

            var (pairFlags, pairCompared) = Compare(pair, closes, observations, settings);
            flags.AddRange(pairFlags);
            compared += pairCompared;
            seriesCompared += pairCompared > 0 ? 1 : 0;
        }

        var note = attempted == 0
            ? pairs.Count == 0
                ? "Nothing to compare: no configured Cboe index has a FRED copy."
                : "Nothing to compare: no cboe: closes recorded yet."
            : string.Create(CultureInfo.InvariantCulture, $"Compared {compared} closes across {seriesCompared} series; {flags.Count} finding(s).");
        if (flags.Count > 0)
        {
            LogFindings(flags.Count);
        }

        return new SourcePullResult(new SourceRunCounts(0, 0, 0, 0, 0), [], errors, attempted, flags, note);
    }

    /// <summary>
    /// Flags for one pair. Only dates inside the compared range are considered; FRED's values are
    /// read here and dropped with the run. <c>Compared</c> counts dates both sides hold.
    /// </summary>
    internal static (IReadOnlyList<string> Flags, int Compared) Compare(
        FredCrossCheckPair pair,
        IReadOnlyDictionary<DateOnly, double> closes,
        IReadOnlyList<FredObservation> observations,
        FredOptions settings)
    {
        var compared = 0;
        var (first, last) = (closes.Keys.Min(), closes.Keys.Max());
        var flags = new List<string>();
        foreach (var observation in observations.Where(observation => observation.Date >= first && observation.Date <= last).OrderBy(observation => observation.Date))
        {
            if (observation.Value is not { } fred)
            {
                continue;
            }

            var date = observation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!closes.TryGetValue(observation.Date, out var cboe))
            {
                flags.Add($"{pair.CboeSymbol} {date}: FRED {pair.FredSeriesId} reports a close that {pair.CboeSourceIdentifier} lacks");
                continue;
            }

            compared++;
            if (Math.Abs(fred - cboe) > settings.Tolerance(cboe))
            {
                flags.Add($"{pair.CboeSymbol} {date}: FRED {pair.FredSeriesId} disagrees with {pair.CboeSourceIdentifier} beyond tolerance");
            }
        }

        return (flags, compared);
    }

    /// <summary>
    /// The newest <paramref name="days"/> New York dates the Cboe adapter recorded for the index, with
    /// the first recorded close of each (rows of other providers do not count).
    /// </summary>
    private async Task<IReadOnlyDictionary<DateOnly, double>> RecentCboeClosesAsync(
        FredCrossCheckPair pair, int days, CancellationToken cancellationToken)
    {
        await using var context = reads.Open();
        var rows = await context.Signals
            .Where(row => row.SourceIdentifier == pair.CboeSourceIdentifier && row.Provider == CboeOptions.SourceName && row.Value != null)
            .OrderByDescending(row => row.ObservedAtMs)
            .ThenBy(row => row.Sequence)
            .Take(days * 4)
            .Select(row => new { row.ObservedAtMs, row.Value })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var closes = new Dictionary<DateOnly, double>();
        foreach (var row in rows)
        {
            var date = MarketTime.NewYorkDate(DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs));
            if (!closes.ContainsKey(date) && closes.Count < days)
            {
                closes[date] = row.Value!.Value;
            }
        }

        return closes;
    }

    /// <summary>The scheduler logs and stores any error text that escapes <see cref="PullAsync"/> through this.</summary>
    public string Redact(string text) => client.Redact(text);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FRED cross-check: {Count} finding(s); see the source status")]
    private partial void LogFindings(int count);
}
