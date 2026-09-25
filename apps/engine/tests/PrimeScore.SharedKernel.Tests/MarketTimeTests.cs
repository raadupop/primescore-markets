namespace PrimeScore.SharedKernel.Tests;

public sealed class MarketTimeTests
{
    [Theory]
    [InlineData(2018, 2, 5, 21)]
    [InlineData(2026, 9, 22, 20)]
    [InlineData(2026, 3, 9, 20)]
    [InlineData(2026, 11, 2, 21)]
    public void The_16_15_New_York_close_is_21_15_UTC_in_winter_and_20_15_UTC_in_summer(int year, int month, int day, int utcHour) =>
        Assert.Equal(new DateTimeOffset(year, month, day, utcHour, 15, 0, TimeSpan.Zero),
            MarketTime.AtNewYork(new DateOnly(year, month, day), new TimeOnly(16, 15)));

    [Theory]
    [InlineData("2026-02-27", "2026-03-02", 1)]
    [InlineData("2026-02-26", "2026-02-27", 1)]
    [InlineData("2026-02-23", "2026-03-02", 5)]
    [InlineData("2026-03-02", "2026-02-27", -1)]
    [InlineData("2026-02-28", "2026-03-01", 0)]
    public void Business_days_between_Friday_and_Monday_is_one(string from, string to, int expected) =>
        Assert.Equal(expected, MarketTime.BusinessDaysBetween(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("2026-02-27", 1, "2026-03-02")]
    [InlineData("2026-03-02", -1, "2026-02-27")]
    [InlineData("2026-02-25", 2, "2026-02-27")]
    public void Adding_business_days_skips_weekends(string start, int days, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            MarketTime.AddBusinessDays(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), days));

    [Theory]
    [InlineData(2025, "2025-01-01,2025-01-09,2025-01-20,2025-02-17,2025-04-18,2025-05-26,2025-06-19,2025-07-04,2025-09-01,2025-11-27,2025-12-25")]
    [InlineData(2026, "2026-01-01,2026-01-19,2026-02-16,2026-04-03,2026-05-25,2026-06-19,2026-07-03,2026-09-07,2026-11-26,2026-12-25")]
    [InlineData(2022, "2022-01-17,2022-02-21,2022-04-15,2022-05-30,2022-06-20,2022-07-04,2022-09-05,2022-11-24,2022-12-26")]
    [InlineData(2018, "2018-01-01,2018-01-15,2018-02-19,2018-03-30,2018-05-28,2018-07-04,2018-09-03,2018-11-22,2018-12-05,2018-12-25")]
    public void The_weekday_closures_match_the_published_NYSE_holiday_list(int year, string expected)
    {
        var closures = Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365)
            .Select(offset => new DateOnly(year, 1, 1).AddDays(offset))
            .Where(MarketTime.IsExchangeHoliday)
            .Select(date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected.Split(','), closures);
    }

    [Theory]
    [InlineData("2021-12-31", true)]
    [InlineData("2021-12-24", false)]
    [InlineData("2021-07-05", false)]
    [InlineData("2027-06-18", false)]
    [InlineData("2021-06-18", true)]
    public void Weekend_holidays_move_to_the_nearest_weekday_except_New_Years_Day_on_a_Saturday(string date, bool open) =>
        Assert.Equal(open, MarketTime.IsTradingDay(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("2025-04-17", "2025-04-21", 1)]
    [InlineData("2025-04-17", "2025-04-22", 2)]
    [InlineData("2025-12-24", "2025-12-26", 1)]
    [InlineData("2025-04-21", "2025-04-17", -1)]
    public void Trading_days_between_skip_Good_Friday_and_Christmas(string from, string to, int expected) =>
        Assert.Equal(expected, MarketTime.TradingDaysBetween(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Adding_one_trading_day_to_the_Thursday_before_Good_Friday_is_Monday() =>
        Assert.Equal(new DateOnly(2025, 4, 21), MarketTime.AddTradingDays(new DateOnly(2025, 4, 17), 1));
}

