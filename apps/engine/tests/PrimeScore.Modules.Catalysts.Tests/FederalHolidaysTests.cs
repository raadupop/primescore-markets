using PrimeScore.Modules.Catalysts.Sources.Claims;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>Federal holidays on their observed days, which move the claims release.</summary>
public sealed class FederalHolidaysTests
{
    [Fact]
    public void The_2026_holidays_fall_on_their_observed_days()
    {
        // 1 January 2026 is a Thursday. Third Mondays: 19 January, 16 February; last Monday of May: 25
        // (31 May is a Sunday). 19 June is a Friday; 4 July a Saturday, observed Friday 3 July. First
        // Monday of September: 7; second of October: 12. 11 November is a Wednesday; the fourth
        // Thursday of November is the 26th; 25 December is a Friday.
        Assert.Equal(
            [
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 19), new DateOnly(2026, 2, 16), new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 19), new DateOnly(2026, 7, 3), new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 12),
                new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 26), new DateOnly(2026, 12, 25),
            ],
            FederalHolidays.ObservedIn(2026).Select(holiday => holiday.Date));
    }

    [Fact]
    public void New_Years_Day_on_a_Saturday_is_observed_on_the_last_day_of_the_year_before()
    {
        // 1 January 2022 was a Saturday.
        Assert.True(FederalHolidays.IsObserved(new DateOnly(2021, 12, 31)));
        Assert.False(FederalHolidays.IsObserved(new DateOnly(2022, 1, 1)));
    }

    [Fact]
    public void Juneteenth_counts_from_2021()
    {
        Assert.False(FederalHolidays.IsObserved(new DateOnly(2020, 6, 19)));

        // 19 June 2021 was a Saturday, observed Friday 18 June.
        Assert.True(FederalHolidays.IsObserved(new DateOnly(2021, 6, 18)));
        Assert.True(FederalHolidays.IsObserved(new DateOnly(2025, 6, 19)));
    }
}
