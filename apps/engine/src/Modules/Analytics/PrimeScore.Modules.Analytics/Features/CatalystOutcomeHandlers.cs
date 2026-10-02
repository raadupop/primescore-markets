using PrimeScore.Modules.Analytics.Catalysts;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Features;

/// <summary>
/// The event record of one catalyst family (ADR-0013), computed on read from the current calendar
/// and the <c>cboe:</c>-recorded closes only: VIX9D, VIX and SPX for the S&amp;P families, OVX for
/// WPSR and OPEC. Missing closes are counted, never filled.
/// </summary>
internal sealed class GetCatalystOutcomesHandler(
    IQueryHandler<GetCatalysts, CatalystList> catalysts,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    IClock clock) : IQueryHandler<GetCatalystOutcomes, CatalystOutcomeReport>
{
    /// <summary>VIX9D's horizon in calendar days.</summary>
    public const int WindowDays = 9;

    /// <summary>Rows in the "latest" summary.</summary>
    public const int LatestCount = 12;

    /// <summary>FOMC and CPI events on or before this date were used by the 2026-09-27 research (ADR-0013).</summary>
    public static readonly DateOnly ExaminedThrough = new(2026, 9, 27);

    private static readonly double PricedScale = Math.Sqrt(WindowDays / 365.0);
    private static readonly double StraddleFactor = Math.Sqrt(2 / Math.PI);
    private static readonly TimeOnly EquityClose = new(16, 0);

    public async Task<CatalystOutcomeReport> HandleAsync(GetCatalystOutcomes query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var computedAt = clock.UtcNow;
        var family = Enum.GetValues<CatalystFamily>().Select(candidate => (CatalystFamily?)candidate)
            .FirstOrDefault(candidate => TermStructureRatio.WireName(candidate!.Value) == query.Family);
        if (family is not { } chosen)
        {
            return Empty(query.Family, [], computedAt);
        }

        var oil = chosen is CatalystFamily.Wpsr or CatalystFamily.Opec;
        var calendar = await catalysts.HandleAsync(new GetCatalysts(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), cancellationToken).ConfigureAwait(false);
        var scheduled = calendar.Catalysts.Where(catalyst => catalyst.Status == CatalystStatus.Scheduled).ToArray();
        var events = scheduled.Where(catalyst => catalyst.Family == chosen).OrderByDescending(catalyst => catalyst.ScheduledAt).ToArray();
        string[] instruments = oil ? ["OVX"] : ["VIX9D", "VIX", "SPX"];
        if (events.Length == 0)
        {
            return Empty(query.Family, instruments, computedAt);
        }

        Closes measure;
        TermStructureRatio? ratio = null;
        Closes? index = null;
        if (oil)
        {
            measure = await ReadAsync("OVX", SourceCategory.MarketData, "IMPLIED_VOLATILITY", DateOnly.MinValue, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            measure = await ReadAsync("VIX9D", SourceCategory.MarketData, "IMPLIED_VOLATILITY:9D", PlaceboCalendar.BaselineStart, cancellationToken).ConfigureAwait(false);
            var thirty = await ReadAsync("VIX", SourceCategory.MarketData, "IMPLIED_VOLATILITY", PlaceboCalendar.BaselineStart, cancellationToken).ConfigureAwait(false);
            index = await ReadAsync("SPX", SourceCategory.CrossAssetFlow, "basket_observation", DateOnly.MinValue, cancellationToken).ConfigureAwait(false);
            ratio = TermStructureRatio.FromCloses(measure.Points, thirty.Points);
        }

        // An event is a row once every series it needs has closed on its event-close date.
        var lastClose = index is null ? measure.Last : Min(measure.Last, index.Last);
        var rows = new List<CatalystOutcome>();
        int beforeLiveStart = 0, missing = 0;
        foreach (var catalyst in events)
        {
            var read = MarketTime.AddTradingDays(catalyst.ScheduledDate, -1);
            var eventClose = EventClose(catalyst);
            if (read < PlaceboCalendar.BaselineStart)
            {
                beforeLiveStart++;
                continue;
            }

            if (lastClose is not { } last || eventClose > last)
            {
                continue;
            }

            var windowEnd = LatestTradingDayOnOrBefore(read.AddDays(WindowDays));
            if (!measure.TryGet(read, out var volBefore) || !measure.TryGet(eventClose, out var volAfter))
            {
                missing++;
                continue;
            }

            var other = scheduled.Count(item => item.CatalystId != catalyst.CatalystId
                && PlaceboCalendar.HaloFamilies.Contains(item.Family)
                && item.ScheduledDate > read && item.ScheduledDate <= read.AddDays(WindowDays));
            var examined = (chosen is CatalystFamily.Fomc or CatalystFamily.Cpi) && catalyst.ScheduledDate <= ExaminedThrough;
            if (oil)
            {
                rows.Add(new CatalystOutcome(catalyst.CatalystId, catalyst.ScheduledAt, catalyst.TimeAnnounced, read, eventClose, windowEnd,
                    false, null, null, null, null, null, null, volAfter - volBefore, other, examined, catalyst.NeverRescheduled));
                continue;
            }

            if (!index!.TryGet(read, out var spxRead) || !index.TryGet(eventClose, out var spxEvent))
            {
                missing++;
                continue;
            }

            var open = windowEnd > index.Last;
            double? actual = null;
            if (!open)
            {
                if (!index.TryGet(windowEnd, out var spxEnd))
                {
                    missing++;
                    continue;
                }

                actual = (spxEnd / spxRead) - 1;
            }

            var priced = volBefore / 100 * PricedScale;
            rows.Add(new CatalystOutcome(
                catalyst.CatalystId,
                catalyst.ScheduledAt,
                catalyst.TimeAnnounced,
                read,
                eventClose,
                windowEnd,
                open,
                ratio!.TryGet(read, out var before) ? before : null,
                priced,
                actual,
                actual is { } inside ? Math.Abs(inside) <= priced : null,
                actual is { } below ? Math.Abs(below) < StraddleFactor * priced : null,
                (spxEvent / spxRead) - 1,
                volAfter - volBefore,
                other,
                examined,
                catalyst.NeverRescheduled));
        }

        var complete = rows.Where(row => !row.WindowOpen).ToArray();
        return new CatalystOutcomeReport(
            query.Family,
            instruments,
            rows,
            rows.Count(row => row.WindowOpen),
            beforeLiveStart,
            missing,
            Summarise(complete, oil),
            Summarise(complete.Take(LatestCount).ToArray(), oil),
            computedAt);
    }

    /// <summary>The first NYSE trading day on or after the event's date whose 16:00 New York close comes after the event.</summary>
    internal static DateOnly EventClose(CatalystView catalyst)
    {
        var date = catalyst.ScheduledDate;
        if (MarketTime.IsTradingDay(date) && MarketTime.AtNewYork(date, EquityClose) > catalyst.ScheduledAt)
        {
            return date;
        }

        return MarketTime.AddTradingDays(date, 1);
    }

    internal static DateOnly LatestTradingDayOnOrBefore(DateOnly date) =>
        MarketTime.IsTradingDay(date) ? date : MarketTime.AddTradingDays(date, -1);

    internal static CatalystOutcomeSummary Summarise(IReadOnlyList<CatalystOutcome> rows, bool oil)
    {
        var fell = rows.Count(row => row.VolatilityChange < 0);
        var volatility = Median(rows.Select(row => row.VolatilityChange));
        if (oil)
        {
            return new CatalystOutcomeSummary(rows.Count, null, null, null, null, null, volatility, fell);
        }

        return new CatalystOutcomeSummary(
            rows.Count,
            Median(rows.Select(row => row.PricedMove!.Value)),
            Median(rows.Select(row => Math.Abs(row.ActualMove!.Value))),
            rows.Count(row => row.InsidePricedRange == true),
            rows.Count(row => row.BelowStraddleEstimate == true),
            Median(rows.Select(row => Math.Abs(row.EventDayMove!.Value))),
            volatility,
            fell);
    }

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static DateOnly? Min(DateOnly? left, DateOnly? right) =>
        left is { } a && right is { } b ? (a < b ? a : b) : null;

    private static CatalystOutcomeReport Empty(string family, IReadOnlyList<string> instruments, DateTimeOffset computedAt)
    {
        var none = new CatalystOutcomeSummary(0, null, null, null, null, null, null, 0);
        return new CatalystOutcomeReport(family, instruments, [], 0, 0, 0, none, none, computedAt);
    }

    private async Task<Closes> ReadAsync(string instrument, SourceCategory category, string variant, DateOnly from, CancellationToken cancellationToken)
    {
        var points = await series.HandleAsync(
            new GetObservationSeries(instrument, category, variant, DateTimeOffset.MaxValue, 1_000_000, SourcePrefix: GetCatalystRatiosHandler.CboePrefix),
            cancellationToken).ConfigureAwait(false);
        return new Closes(points.Points.Select(point => (point.ObservedAt, point.Value)).ToArray(), from);
    }

    /// <summary>One close per NYSE trading day (the earliest recorded); holiday-session prints and dates before <c>from</c> are ignored.</summary>
    private sealed class Closes
    {
        private readonly Dictionary<DateOnly, double> _byDate = [];

        public Closes(IReadOnlyList<(DateTimeOffset ObservedAt, double Value)> points, DateOnly from)
        {
            Points = points;
            foreach (var (observedAt, value) in points)
            {
                var date = MarketTime.NewYorkDate(observedAt);
                if (date >= from && MarketTime.IsTradingDay(date))
                {
                    _byDate.TryAdd(date, value);
                }
            }

            Last = _byDate.Count == 0 ? null : _byDate.Keys.Max();
        }

        public IReadOnlyList<(DateTimeOffset ObservedAt, double Value)> Points { get; }

        public DateOnly? Last { get; }

        public bool TryGet(DateOnly date, out double value) => _byDate.TryGetValue(date, out value);
    }
}
