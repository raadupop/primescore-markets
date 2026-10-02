using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Storage;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Analytics.Features;

/// <summary>
/// The place where a decision meets an outcome (ADR-0008). One state per New York trading day (the
/// last decision recorded for that date); the change runs from the reference close on that date to
/// the close exactly <c>h</c> NYSE trading days later. Holiday-session closes, missing closes and
/// decisions on another reference instrument are left out and counted. Nothing is stored: the report
/// is a pure function of the ledger, so the CLI, the API and the page agree.
/// </summary>
internal sealed class GetForwardOutcomesHandler(
    IQueryHandler<GetActiveSettings, SettingsVersion> settings,
    IQueryHandler<GetDecisionOutcomes, IReadOnlyList<DecisionOutcomePoint>> decisions,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    ReplayReadStore replays,
    IClock clock) : IQueryHandler<GetForwardOutcomes, ForwardOutcomesReport?>
{
    public static readonly IReadOnlyList<int> Horizons = [1, 5, 10, 21];

    /// <summary>State labels recorded before ADR-0008; their DEPLOYs came from the refuted gate.</summary>
    public static readonly IReadOnlySet<string> LegacyStates = new HashSet<string>(StringComparer.Ordinal) { "vol-expansion", "vol-compression", "none" };

    public async Task<ForwardOutcomesReport?> HandleAsync(GetForwardOutcomes query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ContextSettings context;
        IReadOnlyList<DecisionOutcomePoint> points;
        if (query.ReplayId is { } replayId)
        {
            await using var db = replays.Open();
            var id = replayId.ToString("D");
            var row = await db.Replays.AsNoTracking().SingleOrDefaultAsync(replay => replay.ReplayId == id, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return null;
            }

            // The replay's own effective settings decide which instrument its decisions were placed on.
            var replay = CanonicalJson.Deserialize<ReplayView>(row.Payload);
            if (CanonicalJson.Deserialize<EngineSettings>(replay.SettingsJson).Context(query.Context) is not { } replayed)
            {
                return null;
            }

            context = replayed;
            points = CanonicalJson.Deserialize<DecisionView[]>(replay.DecisionsJson)
                .Where(decision => string.Equals(decision.Context, context.Name, StringComparison.OrdinalIgnoreCase)
                    && (query.From is null || decision.AsOf >= query.From) && (query.To is null || decision.AsOf <= query.To))
                .Select((decision, order) => new DecisionOutcomePoint(decision.AsOf, order, decision.Outcome, decision.CompositeScore, decision.Scenario,
                    decision.ReferenceInstrument))
                .ToArray();
        }
        else
        {
            var active = await settings.HandleAsync(new GetActiveSettings(), cancellationToken).ConfigureAwait(false);
            if (active.Settings.Context(query.Context) is not { } live)
            {
                return null;
            }

            context = live;
            points = await decisions.HandleAsync(new GetDecisionOutcomes(context.Name, query.From, query.To), cancellationToken).ConfigureAwait(false);
        }

        var closes = await series.HandleAsync(new GetObservationSeries(context.ReferenceInstrument, SourceCategory.MarketData, "IMPLIED_VOLATILITY",
            DateTimeOffset.MaxValue, 1_000_000), cancellationToken).ConfigureAwait(false);
        return Compute(context.Name, context.ReferenceInstrument, points, closes.Points.Select(point => (point.ObservedAt, point.Value)).ToArray(),
            clock.UtcNow, query.ReplayId);
    }

    internal static ForwardOutcomesReport Compute(
        string context,
        string instrument,
        IReadOnlyList<DecisionOutcomePoint> points,
        IReadOnlyList<(DateTimeOffset ObservedAt, double Value)> closes,
        DateTimeOffset computedAt,
        Guid? replayId = null)
    {
        // One close per NYSE trading day, the earliest recorded (the observation-series convention).
        // Holiday-session prints (Cboe publishes some) are neither a base nor a target.
        var closeByDate = new Dictionary<DateOnly, double>();
        foreach (var (observedAt, value) in closes)
        {
            var date = MarketTime.NewYorkDate(observedAt);
            if (MarketTime.IsTradingDay(date))
            {
                closeByDate.TryAdd(date, value);
            }
        }

        // The last decision of each date placed on this instrument, in date order.
        var own = points.Where(point => string.Equals(point.ReferenceInstrument, instrument, StringComparison.OrdinalIgnoreCase)).ToArray();
        var otherDates = points.Select(point => MarketTime.NewYorkDate(point.AsOf)).ToHashSet();
        var lastByDate = new SortedDictionary<DateOnly, DecisionOutcomePoint>();
        foreach (var point in own.OrderBy(point => point.AsOf).ThenBy(point => point.LedgerSequence))
        {
            lastByDate[MarketTime.NewYorkDate(point.AsOf)] = point;
        }

        otherDates.ExceptWith(lastByDate.Keys);
        var rows = lastByDate
            .Where(pair => closeByDate.ContainsKey(pair.Key))
            .Select(pair => new Row(pair.Key, pair.Value))
            .ToArray();
        var horizons = Horizons.Select(horizon => Horizon(horizon, rows, closeByDate)).ToArray();
        var states = rows.GroupBy(row => row.Point.State, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => State(group.Key, group.ToArray(), closeByDate))
            .ToArray();
        return new ForwardOutcomesReport(
            context, instrument, replayId,
            rows.Length == 0 ? null : rows[0].Date, rows.Length == 0 ? null : rows[^1].Date,
            rows.Length,
            rows.Count(IsDeploy),
            rows.Count(row => LegacyStates.Contains(row.Point.State)),
            lastByDate.Count - rows.Length,
            otherDates.Count,
            horizons, states, computedAt);
    }

    private sealed record Row(DateOnly Date, DecisionOutcomePoint Point);

    /// <summary>A DEPLOY under the ADR-0008 gate: it recorded an extreme state.</summary>
    private static bool IsDeploy(Row row) =>
        row.Point.Outcome == DecisionOutcome.Deploy && row.Point.State is "extreme_high" or "extreme_low";

    /// <summary>Close exactly <paramref name="horizon"/> NYSE trading days later minus the close on the date; null when either is missing.</summary>
    private static double? Change(Row row, int horizon, Dictionary<DateOnly, double> closes) =>
        closes.TryGetValue(MarketTime.AddTradingDays(row.Date, horizon), out var later) ? later - closes[row.Date] : null;

    private static HorizonOutcome Horizon(int horizon, Row[] rows, Dictionary<DateOnly, double> closes)
    {
        var all = rows.Select(row => (Row: row, Change: Change(row, horizon, closes))).Where(item => item.Change is not null)
            .Select(item => (item.Row, Change: item.Change!.Value)).ToArray();
        var deploy = all.Where(item => IsDeploy(item.Row)).ToArray();
        // Toward the median: down after an extreme high, up after an extreme low.
        var toward = deploy.Select(item => item.Row.Point.State == "extreme_high" ? -item.Change : item.Change).ToArray();
        var reverted = toward.Select(change => change > 0 ? 1.0 : 0.0).ToArray();
        var (low, high) = BlockBootstrap(reverted, block: Math.Max(21, 2 * horizon));
        return new HorizonOutcome(
            horizon,
            deploy.Length,
            all.Length,
            deploy.Length == 0 ? null : Median(deploy.Select(item => Math.Abs(item.Change))),
            all.Length == 0 ? null : Median(all.Select(item => Math.Abs(item.Change))),
            reverted.Length == 0 ? null : reverted.Average(),
            low,
            high,
            all.Length == 0 ? null : all.Count(item => item.Change > 0) / (double)all.Length,
            toward.Length == 0 ? null : toward.Average());
    }

    private static StateOutcome State(string state, Row[] rows, Dictionary<DateOnly, double> closes)
    {
        var five = rows.Select(row => Change(row, 5, closes)).Where(change => change is not null).Select(change => change!.Value).ToArray();
        var month = rows.Select(row => Change(row, 21, closes)).Where(change => change is not null).Select(change => change!.Value).ToArray();
        return new StateOutcome(
            state, rows.Length,
            five.Length == 0 ? null : Median(five), five.Length == 0 ? null : Median(five.Select(Math.Abs)), five.Length == 0 ? null : five.Count(change => change > 0) / (double)five.Length,
            month.Length == 0 ? null : Median(month), month.Length == 0 ? null : Median(month.Select(Math.Abs)), month.Length == 0 ? null : month.Count(change => change > 0) / (double)month.Length);
    }

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new InvalidOperationException("The median of an empty set is undefined.");
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// 95 percent interval of the mean of an ordered 0/1 series under a circular block bootstrap
    /// (blocks of <paramref name="block"/> consecutive values, 1,000 resamples, fixed seed); null below two
    /// blocks. Longer horizons use longer blocks because consecutive windows overlap.
    /// </summary>
    internal static (double? Low, double? High) BlockBootstrap(double[] values, int block = 21, int resamples = 1000, int seed = 20260927)
    {
        if (values.Length < block * 2)
        {
            return (null, null);
        }

        var random = new Random(seed);
        var means = new double[resamples];
        for (var resample = 0; resample < resamples; resample++)
        {
            double sum = 0;
            var drawn = 0;
            while (drawn < values.Length)
            {
                var start = random.Next(values.Length);
                for (var offset = 0; offset < block && drawn < values.Length; offset++, drawn++)
                {
                    sum += values[(start + offset) % values.Length];
                }
            }

            means[resample] = sum / values.Length;
        }

        Array.Sort(means);
        return (means[(int)(0.025 * resamples)], means[(int)(0.975 * resamples) - 1]);
    }
}
