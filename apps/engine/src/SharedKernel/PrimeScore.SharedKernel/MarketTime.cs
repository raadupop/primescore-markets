namespace PrimeScore.SharedKernel;

/// <summary>
/// US market wall-clock conversions and business-day arithmetic. Business days are Monday to
/// Friday, matching the classifier's <c>business_day</c> cadence; exchange holidays are not
/// modelled (a documented limitation).
/// </summary>
public static class MarketTime
{
    public static TimeZoneInfo NewYork { get; } = FindNewYork();

    /// <summary>The UTC instant of a New York wall-clock time on a date (DST-aware).</summary>
    public static DateTimeOffset AtNewYork(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = NewYork.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateOnly NewYorkDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, NewYork).DateTime);

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
