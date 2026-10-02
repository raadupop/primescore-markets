namespace PrimeScore.Modules.Catalysts.Sources.Claims;

/// <summary>
/// US federal holidays (5 U.S.C. 6103) on the days they are observed: a holiday that falls on a
/// Saturday is observed the Friday before, one on a Sunday the Monday after. Juneteenth counts from
/// 2021, the year it became a federal holiday. Not the NYSE calendar in <c>MarketTime</c>: the
/// exchange keeps Good Friday and trades on Columbus and Veterans Day, and the claims release
/// follows the federal calendar.
/// </summary>
internal static class FederalHolidays
{
    /// <summary>True when a federal holiday is observed on <paramref name="date"/>.</summary>
    public static bool IsObserved(DateOnly date) =>
        ObservedIn(date.Year).Any(holiday => holiday.Date == date)
        || (date.Year < 9999 && ObservedIn(date.Year + 1).Any(holiday => holiday.Date == date)); // 1 January on a Saturday is observed on 31 December

    /// <summary>The holidays of <paramref name="year"/>, each on its observed day.</summary>
    public static IReadOnlyList<(DateOnly Date, string Name)> ObservedIn(int year)
    {
        var holidays = new List<(DateOnly, string)>
        {
            (Observed(new DateOnly(year, 1, 1)), "New Year's Day"),
            (Nth(year, 1, DayOfWeek.Monday, 3), "Birthday of Martin Luther King, Jr."),
            (Nth(year, 2, DayOfWeek.Monday, 3), "Washington's Birthday"),
            (Last(year, 5, DayOfWeek.Monday), "Memorial Day"),
        };
        if (year >= 2021)
        {
            holidays.Add((Observed(new DateOnly(year, 6, 19)), "Juneteenth National Independence Day"));
        }

        holidays.AddRange(
        [
            (Observed(new DateOnly(year, 7, 4)), "Independence Day"),
            (Nth(year, 9, DayOfWeek.Monday, 1), "Labor Day"),
            (Nth(year, 10, DayOfWeek.Monday, 2), "Columbus Day"),
            (Observed(new DateOnly(year, 11, 11)), "Veterans Day"),
            (Nth(year, 11, DayOfWeek.Thursday, 4), "Thanksgiving Day"),
            (Observed(new DateOnly(year, 12, 25)), "Christmas Day"),
        ]);
        return holidays;
    }

    private static DateOnly Observed(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(1),
        _ => date,
    };

    /// <summary>The <paramref name="n"/>th <paramref name="weekday"/> of the month.</summary>
    private static DateOnly Nth(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateOnly(year, month, 1);
        return first.AddDays((((int)weekday - (int)first.DayOfWeek + 7) % 7) + (7 * (n - 1)));
    }

    private static DateOnly Last(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return last.AddDays(-(((int)last.DayOfWeek - (int)weekday + 7) % 7));
    }
}
