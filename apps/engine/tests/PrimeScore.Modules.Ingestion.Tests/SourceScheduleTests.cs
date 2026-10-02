using System.Globalization;
using PrimeScore.Modules.Ingestion.Contracts;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>
/// New York schedules converted to UTC per run. EDT = UTC−4, EST = UTC−5; US daylight saving
/// ends Sunday 2026-11-01 02:00 and starts Sunday 2027-03-14 02:00.
/// </summary>
public sealed class SourceScheduleTests
{
    private static readonly SourceSchedule Evening = new([new DailyRun(new TimeOnly(18, 0), SourceDays.EveryDay)], []);

    private static readonly SourceSchedule Hourly = new([], [new PollingWindow(new TimeOnly(18, 0), new TimeOnly(8, 0), TimeSpan.FromHours(1), SourceDays.EveryDay)]);

    [Theory]
    [InlineData("2026-10-30T12:00:00Z", "2026-10-30T22:00:00Z")] // Friday before the change: 18:00 + 4 h
    [InlineData("2026-11-02T12:00:00Z", "2026-11-02T23:00:00Z")] // Monday after it: 18:00 + 5 h
    [InlineData("2027-03-13T12:00:00Z", "2027-03-13T23:00:00Z")] // Saturday before spring forward: + 5 h
    [InlineData("2027-03-14T12:00:00Z", "2027-03-14T22:00:00Z")] // the Sunday itself, after 02:00: + 4 h
    public void An_18_00_New_York_run_follows_daylight_saving(string after, string expected) =>
        Assert.Equal(At(expected), Evening.NextAfter(At(after)));

    [Fact]
    public void The_fall_back_night_window_has_fifteen_hourly_slots()
    {
        // 2026-10-31 18:00 EDT = 22:00Z to 2026-11-01 08:00 EST = 13:00Z: 15 h, slots 22:00Z … 12:00Z.
        var slots = Slots(Hourly, At("2026-10-31T21:59:00Z"), At("2026-11-01T13:00:00Z"));

        Assert.Equal(15, slots.Count);
        Assert.Equal(At("2026-10-31T22:00:00Z"), slots[0]);
        Assert.Equal(At("2026-11-01T12:00:00Z"), slots[^1]);
        Assert.Equal(At("2026-11-01T06:00:00Z"), Hourly.NextAfter(At("2026-11-01T05:30:00Z")));

        // After the last slot the next window opens at 18:00 EST = 23:00Z.
        Assert.Equal(At("2026-11-01T23:00:00Z"), Hourly.NextAfter(At("2026-11-01T12:00:00Z")));
    }

    [Fact]
    public void The_spring_forward_night_window_has_thirteen_hourly_slots()
    {
        // 2027-03-13 18:00 EST = 23:00Z to 2027-03-14 08:00 EDT = 12:00Z: 13 h, slots 23:00Z … 11:00Z.
        var slots = Slots(Hourly, At("2027-03-13T22:59:00Z"), At("2027-03-14T12:00:00Z"));

        Assert.Equal(13, slots.Count);
        Assert.Equal(At("2027-03-14T11:00:00Z"), slots[^1]);

        // Next window: 2027-03-14 18:00 EDT = 22:00Z.
        Assert.Equal(At("2027-03-14T22:00:00Z"), Hourly.NextAfter(At("2027-03-14T11:30:00Z")));
    }

    [Fact]
    public void A_polling_window_continues_across_midnight_and_closes_at_its_end()
    {
        var schedule = new SourceSchedule([], [new PollingWindow(new TimeOnly(18, 0), new TimeOnly(8, 0), TimeSpan.FromMinutes(15), SourceDays.EveryDay)]);

        // 23:59 EDT on 09-28 = 03:59Z; window opened 22:00Z; k = floor(5 h 59 min / 15 min) + 1 = 24 → 22:00Z + 6 h = 04:00Z.
        Assert.Equal(At("2026-09-29T04:00:00Z"), schedule.NextAfter(At("2026-09-29T03:59:00Z")));

        // Last slot 07:45 EDT = 11:45Z; 08:00 EDT closes it; next opens 18:00 EDT = 22:00Z.
        Assert.Equal(At("2026-09-29T11:45:00Z"), schedule.NextAfter(At("2026-09-29T11:40:00Z")));
        Assert.Equal(At("2026-09-29T22:00:00Z"), schedule.NextAfter(At("2026-09-29T11:50:00Z")));
    }

    [Fact]
    public void Day_filters_skip_weekends_and_exchange_holidays()
    {
        var weekdays = new SourceSchedule([new DailyRun(new TimeOnly(8, 15), SourceDays.Weekdays)], []);
        var tradingDays = new SourceSchedule([new DailyRun(new TimeOnly(8, 15), SourceDays.TradingDays)], []);

        // Saturday 2026-10-03 → Monday 2026-10-05 08:15 EDT = 12:15Z.
        Assert.Equal(At("2026-10-05T12:15:00Z"), weekdays.NextAfter(At("2026-10-03T13:00:00Z")));

        // Wednesday 2026-11-25 09:00 EST, Thanksgiving Thursday skipped → Friday 08:15 EST = 13:15Z.
        Assert.Equal(At("2026-11-27T13:15:00Z"), tradingDays.NextAfter(At("2026-11-25T14:00:00Z")));
    }

    [Theory]
    // Weekdays, 18:00 to 08:00 hourly; EDT, so windows open 22:00Z and close 12:00Z the next day.
    [InlineData("Weekdays", "2026-10-02T03:30:00Z", "2026-10-02T04:00:00Z")] // Thu 23:30: Thursday's window, 22:00Z + 6 h
    [InlineData("Weekdays", "2026-10-03T03:30:00Z", "2026-10-03T04:00:00Z")] // Fri 23:30: Friday's window runs into Saturday
    [InlineData("Weekdays", "2026-10-03T12:00:00Z", "2026-10-05T22:00:00Z")] // Friday's closed; none opens Sat or Sun → Mon 18:00
    [InlineData("Weekdays", "2026-10-05T03:30:00Z", "2026-10-05T22:00:00Z")] // Sun 23:30 is Monday's date, but no window opened Sunday
    // NYSE trading days; EST, so windows open 23:00Z and close 13:00Z. Thanksgiving is Thursday 2026-11-26.
    [InlineData("TradingDays", "2026-11-26T11:30:00Z", "2026-11-26T12:00:00Z")] // Wednesday's window still runs on Thanksgiving morning
    [InlineData("TradingDays", "2026-11-26T13:00:00Z", "2026-11-27T23:00:00Z")] // none opens Thanksgiving evening → Friday 18:00
    public void A_window_day_filter_applies_to_the_date_the_window_opens(string days, string after, string expected)
    {
        var schedule = new SourceSchedule([], [new PollingWindow(new TimeOnly(18, 0), new TimeOnly(8, 0), TimeSpan.FromHours(1), Enum.Parse<SourceDays>(days))]);

        Assert.Equal(At(expected), schedule.NextAfter(At(after)));
    }

    [Fact]
    public void A_wall_time_that_does_not_exist_resolves_to_the_standard_offset()
    {
        var schedule = new SourceSchedule([new DailyRun(new TimeOnly(2, 30), SourceDays.EveryDay)], []);

        // 2027-03-14 02:30 does not exist; standard offset −5 → 07:30Z (03:30 EDT).
        Assert.Equal(At("2027-03-14T07:30:00Z"), schedule.NextAfter(At("2027-03-14T05:00:00Z")));
    }

    [Fact]
    public void A_wall_time_that_occurs_twice_runs_once_at_the_standard_offset()
    {
        var schedule = new SourceSchedule([new DailyRun(new TimeOnly(1, 30), SourceDays.EveryDay)], []);

        // 2026-11-01 01:30 occurs at 05:30Z (EDT) and 06:30Z (EST); the standard offset gives 06:30Z only.
        Assert.Equal(At("2026-11-01T06:30:00Z"), schedule.NextAfter(At("2026-11-01T04:00:00Z")));
        Assert.Equal(At("2026-11-01T06:30:00Z"), schedule.NextAfter(At("2026-11-01T05:00:00Z")));
    }

    [Fact]
    public void The_earliest_run_across_daily_runs_and_windows_wins()
    {
        var schedule = new SourceSchedule(
            [new DailyRun(new TimeOnly(8, 15), SourceDays.EveryDay)],
            [new PollingWindow(new TimeOnly(18, 0), new TimeOnly(8, 0), TimeSpan.FromMinutes(15), SourceDays.EveryDay)]);

        // 08:00 EDT = 12:00Z closes the window; the daily 08:15 EDT run = 12:15Z comes next.
        Assert.Equal(At("2026-09-29T12:15:00Z"), schedule.NextAfter(At("2026-09-29T11:50:00Z")));
    }

    [Fact]
    public void Describe_names_times_days_and_intervals_in_New_York_time()
    {
        var schedule = new SourceSchedule(
            [new DailyRun(new TimeOnly(8, 15), SourceDays.TradingDays)],
            [new PollingWindow(new TimeOnly(18, 0), new TimeOnly(8, 0), TimeSpan.FromMinutes(15), SourceDays.EveryDay)]);

        Assert.Equal(
            "daily at 08:15, NYSE trading days; every 15 min from 18:00 to 08:00 next day, every day (New York time)",
            schedule.Describe());
        Assert.Equal("on request only", SourceSchedule.OnRequest.Describe());
        Assert.Null(SourceSchedule.OnRequest.NextAfter(At("2026-09-29T12:00:00Z")));
    }

    [Fact]
    public void A_polling_interval_that_is_not_positive_is_refused()
    {
        var schedule = new SourceSchedule([], [new PollingWindow(new TimeOnly(18, 0), new TimeOnly(8, 0), TimeSpan.Zero, SourceDays.EveryDay)]);

        Assert.Throws<InvalidOperationException>(() => schedule.NextAfter(At("2026-09-29T12:00:00Z")));
    }

    private static List<DateTimeOffset> Slots(SourceSchedule schedule, DateTimeOffset from, DateTimeOffset before)
    {
        var slots = new List<DateTimeOffset>();
        for (var next = schedule.NextAfter(from); next is { } slot && slot < before; next = schedule.NextAfter(slot))
        {
            slots.Add(slot);
        }

        return slots;
    }

    private static DateTimeOffset At(string instant) => DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
