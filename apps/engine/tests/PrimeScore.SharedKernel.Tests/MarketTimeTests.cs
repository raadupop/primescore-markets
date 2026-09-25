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
}
