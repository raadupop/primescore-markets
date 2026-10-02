using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Features;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary>
/// <see cref="GetCatalystRatiosHandler"/> against strict stubs: the calendar answers only a whole-range
/// read, the series answer only Cboe-sourced VIX9D and VIX read before New York midnight of the latest
/// requested catalyst. Closes: every NYSE trading day 2026-01-02 … 2026-09-24, VIX 20 on even days of
/// the month and 25 on odd days, VIX9D = VIX × 1.20 (ratio +0.20) except on the dates of
/// <see cref="Exceptions"/>.
/// </summary>
public sealed class CatalystRatioHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>VIX9D written by hand: VIX by parity × (1 + ratio).</summary>
    private static readonly Dictionary<DateOnly, double> Exceptions = new()
    {
        [new(2026, 1, 22)] = 17.0,   // 20 × 0.85 → −0.15
        [new(2026, 2, 5)] = 21.75,   // 25 × 0.87 → −0.13
        [new(2026, 3, 5)] = 22.25,   // 25 × 0.89 → −0.11
        [new(2026, 3, 12)] = 18.2,   // 20 × 0.91 → −0.09
        [new(2026, 4, 23)] = 23.25,  // 25 × 0.93 → −0.07
        [new(2026, 5, 7)] = 23.75,   // 25 × 0.95 → −0.05
        [new(2026, 6, 4)] = 19.4,    // 20 × 0.97 → −0.03
        [new(2026, 6, 11)] = 24.75,  // 25 × 0.99 → −0.01
        [new(2026, 7, 23)] = 25.25,  // 25 × 1.01 → +0.01
        [new(2026, 8, 6)] = 20.6,    // 20 × 1.03 → +0.03
        [new(2026, 9, 24)] = 18.9,   // 20 × 0.945 → −0.055, the as-of date
    };

    private static readonly DateOnly[] Fomc2026 =
        [new(2026, 1, 28), new(2026, 3, 18), new(2026, 4, 29), new(2026, 6, 17), new(2026, 7, 29), new(2026, 9, 16), new(2026, 10, 28), new(2026, 12, 9)];

    [Fact]
    public async Task FOMC_2026_10_28_is_ranked_among_ten_placebo_readings_and_unknown_ids_are_left_out()
    {
        var handler = Handler(out var series);

        var result = await handler.HandleAsync(new GetCatalystRatios(["FOMC-2026-10-28", "FOMC-2031-01-01", "WPSR-2026-09-30"]), TestContext.Current.CancellationToken);

        Assert.Equal(2, series.Answered);
        Assert.Equal(Now, result.ComputedAt);
        Assert.Equal(["FOMC-2026-10-28", "WPSR-2026-09-30"], result.Readings.Select(reading => reading.CatalystId));

        // a = Thu 2026-09-24 (the last date with both closes), c = 34 calendar days, not the day before (10-27).
        // Past Wednesday FOMC events: 01-28, 03-18, 04-29, 06-17, 07-29, 09-16 → 18 placebo days p = D' − 7/14/21, all
        // Wednesday sessions; q = p − 34 is the Thursday five weeks earlier:
        //   2025-12-04, 12-11, 12-18 (before the closes: missing 3); 2026-01-22, [01-29], 02-05; 03-05, 03-12, [03-19];
        //   04-23, [04-30], 05-07; 06-04, 06-11, [06-18]; 07-23, [07-30], 08-06.
        // The five bracketed dates are the Thursday after an FOMC meeting, inside its halo: excluded (5), placebo days 13.
        // Baseline (n = 10): −0.15, −0.13, −0.11, −0.09, −0.07, −0.05, −0.03, −0.01, +0.01, +0.03; at or below −0.055: 5 → 0.5.
        var fomc = result.Readings[0];
        Assert.Equal(((DateOnly?)new DateOnly(2026, 9, 24), (int?)34, false), (fomc.AsOf, fomc.CalendarDaysBefore, fomc.DayBefore));
        Assert.Equal(-0.055, fomc.Value!.Value, 12);
        Assert.Equal((10, 13, 5, 0, 3), (fomc.BaselineCount, fomc.PlaceboDays, fomc.HaloExcluded, fomc.NoSession, fomc.MissingCloses));
        Assert.Equal(0.5, fomc.BaselinePercentile);
        Assert.Null(fomc.NoBaselineReason);

        // Earliest considered read date 2025-12-04 precedes the first recorded FOMC (2026-01-28); no other family is recorded.
        Assert.Equal(["FOMC", "CPI", "NFP", "GDP", "PCE", "OPEC"], fomc.UnscreenedFamilies);

        // WPSR on Wed 2026-09-30: value from Thu 09-24 (c = 6), no baseline.
        var wpsr = result.Readings[1];
        Assert.Equal(((DateOnly?)new DateOnly(2026, 9, 24), (int?)6), (wpsr.AsOf, wpsr.CalendarDaysBefore));
        Assert.Equal(-0.055, wpsr.Value!.Value, 12);
        Assert.Equal((0, null), (wpsr.BaselineCount, wpsr.BaselinePercentile));
        Assert.Equal("weekly release: every same weekday is an event day", wpsr.NoBaselineReason);
    }

    [Fact]
    public async Task No_known_id_reads_no_series()
    {
        var handler = Handler(out var series);

        var result = await handler.HandleAsync(new GetCatalystRatios(["CPI-2026-10-14"]), TestContext.Current.CancellationToken);

        Assert.Empty(result.Readings);
        Assert.Equal(0, series.Calls);
    }

    private static GetCatalystRatiosHandler Handler(out StrictSeries series)
    {
        var calendar = Fomc2026.Select(date => Catalyst(CatalystFamily.Fomc, date, new TimeOnly(14, 0)))
            .Append(Catalyst(CatalystFamily.Wpsr, new DateOnly(2026, 9, 30), new TimeOnly(10, 30)))
            .ToArray();
        series = new StrictSeries(MarketTime.AtNewYork(new DateOnly(2026, 10, 28), TimeOnly.MinValue));
        return new GetCatalystRatiosHandler(new StrictCalendar(calendar), series, new FixedClock(Now));
    }

    private static CatalystView Catalyst(CatalystFamily family, DateOnly date, TimeOnly time)
    {
        var id = $"{family.ToString().ToUpperInvariant()}-{date:yyyy-MM-dd}";
        var at = MarketTime.AtNewYork(date, time);
        return new CatalystView(id, family, id, null, at, date, true, CatalystStatus.Scheduled, null, false, 1, true, false, null,
            Now, null, new CatalystSourceView("test", CatalystSourceKind.Listing, "https://example.invalid/", Now, null), 1, "hash");
    }

    private sealed class StrictCalendar(IReadOnlyList<CatalystView> catalysts) : IQueryHandler<GetCatalysts, CatalystList>
    {
        public Task<CatalystList> HandleAsync(GetCatalysts query, CancellationToken cancellationToken) =>
            Task.FromResult(new CatalystList(
                query == new GetCatalysts(DateTimeOffset.MinValue, DateTimeOffset.MaxValue) ? catalysts : [], Now));
    }

    private sealed class StrictSeries(DateTimeOffset before) : IQueryHandler<GetObservationSeries, ObservationSeries>
    {
        public int Calls { get; private set; }

        public int Answered { get; private set; }

        public Task<ObservationSeries> HandleAsync(GetObservationSeries query, CancellationToken cancellationToken)
        {
            Calls++;
            var nineDay = query is { Instrument: "VIX9D", Variant: "IMPLIED_VOLATILITY:9D" };
            var thirtyDay = query is { Instrument: "VIX", Variant: "IMPLIED_VOLATILITY" };
            if (!(nineDay || thirtyDay) || query.Category != SourceCategory.MarketData || query.SourcePrefix != "cboe:" || query.Before != before)
            {
                return Task.FromResult(new ObservationSeries([]));
            }

            Answered++;
            var points = new List<ObservationPoint>();
            for (var date = new DateOnly(2026, 1, 2); date <= new DateOnly(2026, 9, 24); date = date.AddDays(1))
            {
                if (MarketTime.IsTradingDay(date))
                {
                    var vix = date.Day % 2 == 0 ? 20.0 : 25.0;
                    var value = thirtyDay ? vix : Exceptions.TryGetValue(date, out var nine) ? nine : vix * 1.20;
                    points.Add(new ObservationPoint(MarketTime.AtNewYork(date, new TimeOnly(16, 15)), value, Guid.NewGuid(), points.Count + 1));
                }
            }

            return Task.FromResult(new ObservationSeries(points));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
