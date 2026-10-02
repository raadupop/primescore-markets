using PrimeScore.Modules.Analytics.Catalysts;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Features;

/// <summary>The daily brief of one market context (ADR-0016), composed on read; nothing is stored or sent.</summary>
internal sealed class GetDailyBriefHandler(
    IQueryHandler<GetActiveSettings, SettingsVersion> settings,
    IQueryHandler<GetDecisions, IReadOnlyList<DecisionView>> decisions,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    IQueryHandler<GetCatalysts, CatalystList> catalysts,
    IQueryHandler<GetCatalystRatios, CatalystRatioList> ratios,
    IQueryHandler<GetCatalystOutcomes, CatalystOutcomeReport> eventRecord,
    IClock clock) : IQueryHandler<GetDailyBrief, DailyBrief>
{
    public const int AheadTradingDays = 10;
    public const int NewListed = 20;
    public const int GridHistory = 1260;
    public const double GridLowBelow = 0.3;
    public const double GridHighAbove = 0.7;

    /// <summary>Cboe series of the cross-asset grid, all 30-day implied volatility indices.</summary>
    public static readonly IReadOnlyList<string> GridInstruments = ["VIX", "VXN", "RVX", "VVIX", "OVX", "GVZ"];

    /// <summary>Decisions read back for the days-in-state run: about two years of trading days.</summary>
    private const int StateLookbackDays = 730;

    private static readonly TimeOnly EquityClose = new(16, 0);

    public async Task<DailyBrief> HandleAsync(GetDailyBrief query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var computedAt = clock.UtcNow;
        var active = await settings.HandleAsync(new GetActiveSettings(), cancellationToken).ConfigureAwait(false);
        var context = active.Settings.Contexts.FirstOrDefault(candidate => candidate.Name == query.Context);
        if (context is null)
        {
            return Empty($"Unknown market '{query.Context}'.", null);
        }

        var latest = await decisions.HandleAsync(new GetDecisions(Context: context.Name, Take: 1), cancellationToken).ConfigureAwait(false);
        if (latest.Count == 0)
        {
            return Empty("No decision is recorded for this market yet.", context.ReferenceInstrument);
        }

        var briefDate = MarketTime.NewYorkDate(latest[0].AsOf);
        var history = await decisions.HandleAsync(
            new GetDecisions(From: MarketTime.AtNewYork(briefDate.AddDays(-StateLookbackDays), TimeOnly.MinValue), To: latest[0].AsOf, Context: context.Name, Take: null),
            cancellationToken).ConfigureAwait(false);
        var state = State(history, briefDate);

        // The market's own index is in the state line, from its recorded decision; ranking it again from
        // Cboe closes alone could show a second, slightly different percentile (ADR-0016).
        var grid = new List<BriefGridRow>();
        foreach (var instrument in GridInstruments.Where(instrument => instrument != context.ReferenceInstrument))
        {
            grid.Add(await GridRowAsync(instrument, briefDate, cancellationToken).ConfigureAwait(false));
        }

        var calendar = await catalysts.HandleAsync(new GetCatalysts(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), cancellationToken).ConfigureAwait(false);
        var scheduled = calendar.Catalysts.Where(catalyst => catalyst.Status == CatalystStatus.Scheduled).ToArray();
        var horizon = MarketTime.AddTradingDays(briefDate, AheadTradingDays);
        var ahead = scheduled
            .Select(catalyst => (Catalyst: catalyst, Close: GetCatalystOutcomesHandler.EventClose(catalyst)))
            .Where(item => item.Close > briefDate && item.Close <= horizon)
            .OrderBy(item => item.Catalyst.ScheduledAt).ThenBy(item => item.Catalyst.CatalystId, StringComparer.Ordinal)
            .ToArray();
        var readings = ahead.Length == 0
            ? new Dictionary<string, CatalystRatioReading>()
            : (await ratios.HandleAsync(new GetCatalystRatios(ahead.Select(item => item.Catalyst.CatalystId).ToArray()), cancellationToken).ConfigureAwait(false))
                .Readings.ToDictionary(reading => reading.CatalystId, StringComparer.Ordinal);

        var records = new Dictionary<CatalystFamily, CatalystOutcomeReport>();
        async Task<CatalystOutcomeReport> RecordAsync(CatalystFamily family)
        {
            if (!records.TryGetValue(family, out var record))
            {
                record = await eventRecord.HandleAsync(new GetCatalystOutcomes(TermStructureRatio.WireName(family)), cancellationToken).ConfigureAwait(false);
                records[family] = record;
            }

            return record;
        }

        var recordRows = new List<BriefRecord>();
        foreach (var family in ahead.Select(item => item.Catalyst.Family).Distinct().OrderBy(family => family))
        {
            var record = await RecordAsync(family).ConfigureAwait(false);
            recordRows.Add(new BriefRecord(TermStructureRatio.WireName(family), family is CatalystFamily.Wpsr or CatalystFamily.Opec, record.Latest12));
        }

        var passed = new List<BriefPassed>();
        foreach (var item in scheduled.Where(catalyst => GetCatalystOutcomesHandler.EventClose(catalyst) == briefDate).OrderBy(catalyst => catalyst.ScheduledAt))
        {
            var record = await RecordAsync(item.Family).ConfigureAwait(false);
            if (record.Events.FirstOrDefault(row => row.CatalystId == item.CatalystId) is { } row)
            {
                passed.Add(new BriefPassed(TermStructureRatio.WireName(item.Family), row));
            }
        }

        // First recorded after the previous trading day's close, scheduled after the brief date.
        var previousClose = MarketTime.AtNewYork(MarketTime.AddTradingDays(briefDate, -1), EquityClose);
        var fresh = scheduled
            .Where(catalyst => catalyst.FirstAnnouncedAt > previousClose && catalyst.ScheduledDate > briefDate)
            .OrderBy(catalyst => catalyst.ScheduledAt).ThenBy(catalyst => catalyst.CatalystId, StringComparer.Ordinal)
            .ToArray();

        return new DailyBrief(
            context.Name,
            context.ReferenceInstrument,
            briefDate,
            state,
            grid,
            ahead.Select(item => View(item.Catalyst, item.Close, readings.GetValueOrDefault(item.Catalyst.CatalystId))).ToArray(),
            recordRows,
            passed,
            fresh.Take(NewListed).Select(catalyst => View(catalyst, GetCatalystOutcomesHandler.EventClose(catalyst), null)).ToArray(),
            fresh.Length,
            null,
            computedAt);

        DailyBrief Empty(string error, string? reference) =>
            new(query.Context, reference, null, null, [], [], [], [], [], 0, error, computedAt);
    }

    /// <summary>The brief date's last decision and the run of trading days back from it in the same state (ADR-0016).</summary>
    internal static BriefState State(IReadOnlyList<DecisionView> newestFirst, DateOnly briefDate)
    {
        // Newest first: the first decision of each date is that date's last.
        var lastPerDate = new Dictionary<DateOnly, DecisionView>();
        foreach (var decision in newestFirst)
        {
            lastPerDate.TryAdd(MarketTime.NewYorkDate(decision.AsOf), decision);
        }

        var today = lastPerDate[briefDate];
        var days = 1;
        string? previous = null;
        for (var date = MarketTime.AddTradingDays(briefDate, -1); ; date = MarketTime.AddTradingDays(date, -1))
        {
            if (!lastPerDate.TryGetValue(date, out var decision))
            {
                break;
            }

            if (decision.Scenario != today.Scenario)
            {
                previous = decision.Scenario;
                break;
            }

            days++;
        }

        return new BriefState(today.DecisionId, today.AsOf, today.ReferenceInstrument, today.MarketObservedIv, today.LevelPercentile, today.Scenario, days, previous);
    }

    /// <summary>The latest Cboe close on or before the brief date, ranked among up to 1,260 prior closes.</summary>
    internal static BriefGridRow GridRow(string instrument, IReadOnlyList<(DateOnly Date, double Close)> closes, DateOnly briefDate)
    {
        var upTo = closes.Where(point => point.Date <= briefDate).ToArray();
        if (upTo.Length == 0)
        {
            return new BriefGridRow(instrument, null, null, null, 0, VolatilityState.Unknown);
        }

        var (date, close) = upTo[^1];
        var prior = upTo[..^1].TakeLast(GridHistory).ToArray();
        double? percentile = prior.Length == 0 ? null : prior.Count(value => value.Close <= close) / (double)prior.Length;
        var regime = percentile switch
        {
            < GridLowBelow => "low_vol",
            > GridHighAbove => "high_vol",
            _ => "normal",
        };
        return new BriefGridRow(instrument, date, close, percentile, prior.Length,
            VolatilityState.Label(percentile, prior.Length, regime, "<=", VolatilityState.DefaultTail));
    }

    private async Task<BriefGridRow> GridRowAsync(string instrument, DateOnly briefDate, CancellationToken cancellationToken)
    {
        var points = await series.HandleAsync(
            new GetObservationSeries(instrument, SourceCategory.MarketData, "IMPLIED_VOLATILITY", DateTimeOffset.MaxValue, 1_000_000, SourcePrefix: GetCatalystRatiosHandler.CboePrefix),
            cancellationToken).ConfigureAwait(false);
        var byDate = new SortedDictionary<DateOnly, double>();
        foreach (var point in points.Points)
        {
            var date = MarketTime.NewYorkDate(point.ObservedAt);
            if (MarketTime.IsTradingDay(date))
            {
                byDate.TryAdd(date, point.Value);
            }
        }

        return GridRow(instrument, byDate.Select(pair => (pair.Key, pair.Value)).ToArray(), briefDate);
    }

    private static BriefCatalyst View(CatalystView catalyst, DateOnly close, CatalystRatioReading? reading) => new(
        catalyst.CatalystId,
        TermStructureRatio.WireName(catalyst.Family),
        catalyst.Title,
        catalyst.ScheduledAt,
        catalyst.TimeAnnounced,
        close,
        reading?.Value,
        reading?.BaselinePercentile,
        catalyst.LedgerSequence);
}
