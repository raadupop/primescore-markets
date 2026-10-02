namespace PrimeScore.SharedKernel;

/// <summary>
/// US market wall-clock conversions and day arithmetic. Business days are Monday to Friday,
/// matching the classifier's <c>business_day</c> cadence. Trading days also skip NYSE full-day
/// closures (<see cref="IsTradingDay"/>); the engine's own windows count trading days
/// (ADR-0004 §4, §5).
/// </summary>
public static class MarketTime
{
    public static TimeZoneInfo NewYork { get; } = FindNewYork();

    private static readonly TimeSpan EasternStandard = TimeSpan.FromHours(-5);

    private static readonly TimeSpan EasternDaylight = TimeSpan.FromHours(-4);

    /// <summary>
    /// The UTC instant of a New York wall-clock time on a date (DST-aware, <see cref="UniformTimeActDaylight"/>
    /// before 1987). A time that does not exist or occurs twice resolves to the standard offset.
    /// </summary>
    public static DateTimeOffset AtNewYork(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = UniformTimeActDaylight(date.Year) is { } daylight
            ? local >= daylight.Start.AddHours(1) && local < daylight.End.AddHours(-1) ? EasternDaylight : EasternStandard
            : NewYork.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateOnly NewYorkDate(DateTimeOffset instant)
    {
        var standard = instant.UtcDateTime + EasternStandard;
        if (UniformTimeActDaylight(standard.Year) is not { } daylight)
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, NewYork).DateTime);
        }

        // Daylight time runs from 02:00 EST at the start to 02:00 EDT (01:00 EST) at the end.
        var inDaylight = standard >= daylight.Start && standard < daylight.End.AddHours(-1);
        return DateOnly.FromDateTime(inDaylight ? standard.AddHours(1) : standard);
    }

    public static bool IsBusinessDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>Business days in <c>[from, to)</c>; negative when <paramref name="to"/> precedes <paramref name="from"/>.</summary>
    public static int BusinessDaysBetween(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return -BusinessDaysBetween(to, from);
        }

        var days = to.DayNumber - from.DayNumber;
        var weeks = days / 7;
        var count = weeks * 5;
        for (var date = from.AddDays(weeks * 7); date < to; date = date.AddDays(1))
        {
            if (IsBusinessDay(date))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The date <paramref name="days"/> business days after (or before, if negative) <paramref name="date"/>.</summary>
    public static DateOnly AddBusinessDays(DateOnly date, int days)
    {
        var step = Math.Sign(days);
        var remaining = Math.Abs(days);
        var current = date;
        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsBusinessDay(current))
            {
                remaining--;
            }
        }

        return current;
    }

    /// <summary>A weekday on which the NYSE is open for a full or partial session.</summary>
    public static bool IsTradingDay(DateOnly date) => IsBusinessDay(date) && !IsExchangeHoliday(date);

    /// <summary>Trading days in <c>[from, to)</c>; negative when <paramref name="to"/> precedes <paramref name="from"/>.</summary>
    public static int TradingDaysBetween(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return -TradingDaysBetween(to, from);
        }

        var count = 0;
        for (var date = from; date < to; date = date.AddDays(1))
        {
            if (IsTradingDay(date))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The date <paramref name="days"/> trading days after (or before, if negative) <paramref name="date"/>.</summary>
    public static DateOnly AddTradingDays(DateOnly date, int days)
    {
        var step = Math.Sign(days);
        var remaining = Math.Abs(days);
        var current = date;
        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsTradingDay(current))
            {
                remaining--;
            }
        }

        return current;
    }

    /// <summary>
    /// NYSE full-day closures on weekdays: the rule-based holidays (NYSE Rule 7.2 as observed
    /// since 1998) and the one-off closures since 2001. A future one-off closure is not known in
    /// advance; that day then reads as a trading day without data.
    /// </summary>
    public static bool IsExchangeHoliday(DateOnly date)
    {
        if (!IsBusinessDay(date))
        {
            return false;
        }

        if (OneOffClosures.Contains(date))
        {
            return true;
        }

        var year = date.Year;
        return date == Observed(new DateOnly(year, 1, 1), saturdayMovesToFriday: false)
            || date == NthWeekday(year, 1, DayOfWeek.Monday, 3)
            || date == NthWeekday(year, 2, DayOfWeek.Monday, 3)
            || date == EasterSunday(year).AddDays(-2)
            || date == LastWeekday(year, 5, DayOfWeek.Monday)
            || (year >= 2022 && date == Observed(new DateOnly(year, 6, 19), saturdayMovesToFriday: true))
            || date == Observed(new DateOnly(year, 7, 4), saturdayMovesToFriday: true)
            || date == NthWeekday(year, 9, DayOfWeek.Monday, 1)
            || date == NthWeekday(year, 11, DayOfWeek.Thursday, 4)
            || date == Observed(new DateOnly(year, 12, 25), saturdayMovesToFriday: true);
    }

    private static readonly HashSet<DateOnly> OneOffClosures =
    [
        new(2001, 9, 11), new(2001, 9, 12), new(2001, 9, 13), new(2001, 9, 14), // September 11
        new(2004, 6, 11),                                                         // President Reagan's funeral
        new(2007, 1, 2),                                                          // President Ford's funeral
        new(2012, 10, 29), new(2012, 10, 30),                                    // Hurricane Sandy
        new(2018, 12, 5),                                                         // President G. H. W. Bush's funeral
        new(2025, 1, 9),                                                          // President Carter's funeral
    ];

    /// <summary>
    /// Sunday holidays move to Monday; Saturday ones to Friday, except New Year's Day, which
    /// the NYSE does not observe on the preceding Friday (that Friday closes a year).
    /// </summary>
    private static DateOnly Observed(DateOnly holiday, bool saturdayMovesToFriday) => holiday.DayOfWeek switch
    {
        DayOfWeek.Sunday => holiday.AddDays(1),
        DayOfWeek.Saturday => saturdayMovesToFriday ? holiday.AddDays(-1) : DateOnly.MinValue,
        _ => holiday,
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek day, int n)
    {
        var first = new DateOnly(year, month, 1);
        return first.AddDays((((int)day - (int)first.DayOfWeek + 7) % 7) + ((n - 1) * 7));
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek day)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return last.AddDays(-(((int)last.DayOfWeek - (int)day + 7) % 7));
    }

    /// <summary>Gregorian Easter Sunday (anonymous Gregorian algorithm).</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// New York daylight time from 1967 to 1986, as local wall-clock bounds (02:00 on each changeover
    /// Sunday): the last Sunday of April to the last Sunday of October, except 6 January 1974 and
    /// 23 February 1975 (energy-crisis starts). Windows' "Eastern Standard Time" applies the 1987–2006
    /// rule to every earlier year, so SPX closes from 1975 would get a different stamp than on Linux,
    /// some before the close; null outside these years, where the system zone is right.
    /// </summary>
    private static (DateTime Start, DateTime End)? UniformTimeActDaylight(int year)
    {
        if (year is < 1967 or > 1986)
        {
            return null;
        }

        var start = year switch
        {
            1974 => new DateOnly(1974, 1, 6),
            1975 => new DateOnly(1975, 2, 23),
            _ => LastWeekday(year, 4, DayOfWeek.Sunday),
        };
        return (start.ToDateTime(new TimeOnly(2, 0)), LastWeekday(year, 10, DayOfWeek.Sunday).ToDateTime(new TimeOnly(2, 0)));
    }

    private static TimeZoneInfo FindNewYork()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        throw new TimeZoneNotFoundException("Neither America/New_York nor Eastern Standard Time is available.");
    }
}
