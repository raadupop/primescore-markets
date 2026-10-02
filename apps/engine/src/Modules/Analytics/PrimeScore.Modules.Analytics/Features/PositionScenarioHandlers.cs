using System.Globalization;
using PrimeScore.Modules.Analytics.Catalysts;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Positions;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Features;

/// <summary>
/// Values an operator-entered S&amp;P 500 option position and replays every past release of each scheduled
/// family before its expiry on it (ADR-0014). Reads only Cboe-recorded closes and the event record;
/// records nothing.
/// </summary>
internal sealed class GetPositionScenariosHandler(
    IQueryHandler<GetCatalysts, CatalystList> catalysts,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    IQueryHandler<GetCatalystOutcomes, CatalystOutcomeReport> eventRecord,
    IClock clock) : IQueryHandler<GetPositionScenarios, PositionScenarioReport>
{
    public const int Multiplier = 100;
    public const int MaxLegs = 4;
    public const int MaxDaysToExpiry = 3 * 365;

    /// <summary>Floor of a scenario volatility, as a fraction (one volatility point).</summary>
    public const double VolatilityFloor = 0.01;

    /// <summary>Cboe's constant maturities in calendar days.</summary>
    private static readonly (string Instrument, string Variant, int Days)[] Tenors =
    [
        ("VIX9D", "IMPLIED_VOLATILITY:9D", 9),
        ("VIX", "IMPLIED_VOLATILITY", 30),
        ("VIX3M", "IMPLIED_VOLATILITY:3M", 93),
        ("VIX6M", "IMPLIED_VOLATILITY:6M", 184),
    ];

    private static readonly CatalystFamily[] ScenarioFamilies =
        [CatalystFamily.Fomc, CatalystFamily.Cpi, CatalystFamily.Nfp, CatalystFamily.Gdp, CatalystFamily.Pce];

    public async Task<PositionScenarioReport> HandleAsync(GetPositionScenarios query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var position = query.Position;
        var computedAt = clock.UtcNow;
        var spx = await ReadAsync("SPX", SourceCategory.CrossAssetFlow, "basket_observation", cancellationToken).ConfigureAwait(false);
        var volatility = new Dictionary<int, IReadOnlyDictionary<DateOnly, double>>();
        foreach (var (instrument, variant, horizon) in Tenors)
        {
            volatility[horizon] = await ReadAsync(instrument, SourceCategory.MarketData, variant, cancellationToken).ConfigureAwait(false);
        }

        DateOnly? asOf = spx.Keys.Where(date => volatility[30].ContainsKey(date)).Select(date => (DateOnly?)date).Max();
        if (asOf is not { } today)
        {
            return Failed("No Cboe SPX and VIX closes are recorded yet; enable the Cboe source in Data and health.", null, null);
        }

        var spy = position.Underlying == "SPY";
        var level = spy ? spx[today] / 10 : spx[today];
        if (Invalid(position, today) is { } problem)
        {
            return Failed(problem, today, level);
        }

        var days = position.Expiry.DayNumber - today.DayNumber;
        var term = new VolatilityTermStructure(Tenors
            .Where(tenor => volatility[tenor.Days].ContainsKey(today))
            .Select(tenor => (tenor.Days, volatility[tenor.Days][today] / 100)));
        var sigma = term.At(days);
        var book = new Book(position);
        var value = book.Value(level, sigma, days);
        var entryCost = position.EntryCost ?? value;
        var (maxLoss, unlimited) = MaxLoss(position, entryCost);

        var events = new List<PositionEventScenarios>();
        var calendar = await catalysts.HandleAsync(new GetCatalysts(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), cancellationToken).ConfigureAwait(false);
        var records = new Dictionary<CatalystFamily, CatalystOutcomeReport>();
        foreach (var catalyst in calendar.Catalysts
            .Where(catalyst => catalyst.Status == CatalystStatus.Scheduled && ScenarioFamilies.Contains(catalyst.Family))
            .OrderBy(catalyst => catalyst.ScheduledAt))
        {
            var close = GetCatalystOutcomesHandler.EventClose(catalyst);
            if (close <= today || close > position.Expiry)
            {
                continue;
            }

            if (!records.TryGetValue(catalyst.Family, out var record))
            {
                record = await eventRecord.HandleAsync(new GetCatalystOutcomes(TermStructureRatio.WireName(catalyst.Family)), cancellationToken).ConfigureAwait(false);
                records[catalyst.Family] = record;
            }

            events.Add(Replay(catalyst, close, today, days, level, sigma, value, book, record));
        }

        return new PositionScenarioReport(
            null, today, level, sigma, days, value,
            book.Delta(level, sigma, days), book.Gamma(level, sigma, days), book.Vega(level, sigma, days) / 100,
            book.Value(level, sigma, days - 1) - value,
            entryCost, maxLoss, unlimited, events, computedAt);

        PositionScenarioReport Failed(string error, DateOnly? on, double? at) =>
            new(error, on, at, null, null, null, null, null, null, null, position.EntryCost, null, false, [], computedAt);
    }

    /// <summary>Every past release of the family, applied at the event close with the option's remaining time (ADR-0014).</summary>
    internal static PositionEventScenarios Replay(
        CatalystView catalyst, DateOnly close, DateOnly today, int daysToExpiry, double level, double sigma, double value, Book book, CatalystOutcomeReport record)
    {
        var daysFrom = close.DayNumber - today.DayNumber;
        var remaining = daysToExpiry - daysFrom;
        var before = book.Value(level, sigma, remaining);
        var moves = new List<double>();
        var vols = new List<double>();
        var totals = new List<double>();
        foreach (var row in record.Events.Where(row => row.EventDayMove is not null && row.PricedMove is not null))
        {
            var moved = level * (1 + row.EventDayMove!.Value);
            var shifted = Math.Max(VolatilityFloor, sigma + (VolatilityShift(row.VolatilityChange, remaining) / 100));
            moves.Add(book.Value(moved, sigma, remaining) - before);
            vols.Add(book.Value(level, shifted, remaining) - before);
            totals.Add(book.Value(moved, shifted, remaining) - before);
        }

        var n = totals.Count;
        return new PositionEventScenarios(
            catalyst.CatalystId,
            TermStructureRatio.WireName(catalyst.Family),
            catalyst.ScheduledAt,
            close,
            daysFrom,
            before - value,
            n,
            n == 0 ? null : moves.Average(),
            n == 0 ? null : vols.Average(),
            n == 0 ? null : totals.Average(),
            Median(totals),
            n == 0 ? null : totals.Count(total => total < 0) / (double)n,
            n == 0 ? null : totals.Min(),
            n == 0 ? null : totals.Max());
    }

    /// <summary>
    /// A past VIX9D change (index points) carried to an option with <paramref name="remainingDays"/> left: in full up
    /// to VIX9D's 9 days, scaled by √(9 / days) beyond, because shorter tenors move more (ADR-0014).
    /// </summary>
    internal static double VolatilityShift(double nineDayChange, int remainingDays) =>
        remainingDays <= 0 ? 0 : nineDayChange * Math.Min(1, Math.Sqrt(9.0 / remainingDays));

    /// <summary>The payoff is piecewise linear in the expiry level, so its minimum lies at zero or a strike unless calls are net short.</summary>
    internal static (double? MaxLoss, bool Unlimited) MaxLoss(PositionInput position, double entryCost)
    {
        if (position.Legs.Where(leg => leg.Right == "C").Sum(leg => leg.Quantity) < 0)
        {
            return (null, true);
        }

        var lowest = double.MaxValue;
        foreach (var level in position.Legs.Select(leg => leg.Strike).Append(0))
        {
            var payoff = 0.0;
            foreach (var leg in position.Legs)
            {
                payoff += leg.Quantity * Multiplier * (leg.Right == "C" ? Math.Max(0, level - leg.Strike) : Math.Max(0, leg.Strike - level));
            }

            lowest = Math.Min(lowest, payoff);
        }

        return (Math.Max(0, entryCost - lowest), false);
    }

    internal static string? Invalid(PositionInput position, DateOnly today)
    {
        if (position.Underlying is not ("SPX" or "SPY"))
        {
            return "The underlying must be SPX or SPY.";
        }

        if (position.Legs.Count is < 1 or > MaxLegs)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Enter one to {MaxLegs} legs.");
        }

        foreach (var leg in position.Legs)
        {
            if (leg.Right is not ("C" or "P"))
            {
                return "Each leg must be a call (C) or a put (P).";
            }

            if (!double.IsFinite(leg.Strike) || leg.Strike <= 0)
            {
                return "Each strike must be a positive number.";
            }

            if (leg.Quantity == 0 || Math.Abs(leg.Quantity) > 10_000)
            {
                return "Each quantity must be a non-zero number of contracts up to 10,000.";
            }
        }

        if (position.Expiry <= today)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The expiry must be after the latest close ({today:yyyy-MM-dd}).");
        }

        if (position.Expiry.DayNumber - today.DayNumber > MaxDaysToExpiry)
        {
            return "The expiry must be within three years.";
        }

        if (!double.IsFinite(position.Rate) || !double.IsFinite(position.DividendYield) || Math.Abs(position.Rate) > 0.25 || Math.Abs(position.DividendYield) > 0.25)
        {
            return "The rate and dividend yield must be between −25% and 25%.";
        }

        return position.EntryCost is { } cost && !double.IsFinite(cost) ? "The entry cost must be a number." : null;
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private async Task<IReadOnlyDictionary<DateOnly, double>> ReadAsync(string instrument, SourceCategory category, string variant, CancellationToken cancellationToken)
    {
        var points = await series.HandleAsync(
            new GetObservationSeries(instrument, category, variant, DateTimeOffset.MaxValue, 1_000_000, SourcePrefix: GetCatalystRatiosHandler.CboePrefix),
            cancellationToken).ConfigureAwait(false);
        var byDate = new Dictionary<DateOnly, double>();
        foreach (var point in points.Points)
        {
            var date = MarketTime.NewYorkDate(point.ObservedAt);
            if (MarketTime.IsTradingDay(date))
            {
                byDate.TryAdd(date, point.Value);
            }
        }

        return byDate;
    }

    /// <summary>The position's legs valued together; dollar figures use the 100 multiplier.</summary>
    internal sealed class Book(PositionInput position)
    {
        public double Value(double level, double volatility, int days) =>
            Sum((call, strike, years) => OptionPricing.Price(call, level, strike, years, volatility, position.Rate, position.DividendYield), days);

        public double Delta(double level, double volatility, int days) =>
            Sum((call, strike, years) => OptionPricing.Delta(call, level, strike, years, volatility, position.Rate, position.DividendYield), days);

        public double Gamma(double level, double volatility, int days) =>
            Sum((_, strike, years) => OptionPricing.Gamma(level, strike, years, volatility, position.Rate, position.DividendYield), days);

        public double Vega(double level, double volatility, int days) =>
            Sum((_, strike, years) => OptionPricing.Vega(level, strike, years, volatility, position.Rate, position.DividendYield), days);

        private double Sum(Func<bool, double, double, double> perUnit, int days)
        {
            var years = Math.Max(0, days) / 365.0;
            return position.Legs.Sum(leg => leg.Quantity * Multiplier * perUnit(leg.Right == "C", leg.Strike, years));
        }
    }
}
