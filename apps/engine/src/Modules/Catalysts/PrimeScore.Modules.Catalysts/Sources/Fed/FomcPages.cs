using System.Globalization;
using System.Text.RegularExpressions;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Fed;

/// <summary>
/// Parses the Federal Reserve's FOMC calendars (ADR-0011): the current page (one panel per year,
/// a month cell and a date cell per meeting, <c>*</c> marking a Summary of Economic Projections)
/// and the historical page of one year (one heading per meeting, SEP when the meeting's materials
/// include projections). The decision is scheduled at 14:00 New York on the meeting's last day.
/// Notation votes and unscheduled meetings are skipped (only pre-announced meetings are
/// catalysts); a cancelled meeting is kept as cancelled. Every row of a panel whose text says its
/// dates are tentative is marked tentative.
/// </summary>
internal static partial class FomcPages
{
    /// <summary>The statement time (ADR-0011; stated from 2016, assumed for 2013–2015).</summary>
    public static readonly TimeOnly DecisionTime = new(14, 0);

    /// <summary>The current calendar; coverage runs from 1 January of its first panel year to 31 December of its last.</summary>
    public static ParsedCalendar ParseCurrent(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var panels = PanelHeading().Matches(html);
        if (panels.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no '<year> FOMC Meetings' panel");
        }

        var rows = new List<ObservedCatalyst>();
        var rejected = new List<string>();
        var skipped = new List<string>();
        var years = new List<int>();
        for (var index = 0; index < panels.Count; index++)
        {
            var year = int.Parse(panels[index].Groups["year"].Value, CultureInfo.InvariantCulture);
            years.Add(year);
            var body = Segment(html, panels[index].Index + panels[index].Length, index + 1 < panels.Count ? panels[index + 1].Index : html.Length);
            var tentative = body.Contains("tentative", StringComparison.OrdinalIgnoreCase);
            var months = MonthCell().Matches(body);
            var parsed = 0;
            for (var row = 0; row < months.Count; row++)
            {
                var start = months[row].Index + months[row].Length;
                var end = row + 1 < months.Count ? months[row + 1].Index : body.Length;
                var date = DateCell().Match(body, start, end - start);
                var month = CalendarText.Clean(months[row].Groups["text"].Value);
                if (!date.Success)
                {
                    rejected.Add(CalendarText.Quote($"{year} {month}"));
                    continue;
                }

                var cell = CalendarText.Clean(date.Groups["text"].Value);
                switch (Meeting(year, month, cell, tentative))
                {
                    case { Skip: { } label }:
                        skipped.Add(label);
                        parsed++;
                        break;
                    case { Row: { } observed }:
                        rows.Add(observed);
                        parsed++;
                        break;
                    default:
                        rejected.Add(CalendarText.Quote($"{year} {month} {cell}"));
                        break;
                }
            }

            if (parsed == 0)
            {
                throw CalendarText.LayoutNotRecognised(string.Create(CultureInfo.InvariantCulture, $"no meeting row of the {year} panel parses"));
            }
        }

        return new ParsedCalendar(rows, rejected, skipped, new DateOnly(years.Min(), 1, 1), new DateOnly(years.Max(), 12, 31));
    }

    /// <summary>One year's historical page (kind Archive); no coverage claim.</summary>
    public static ParsedCalendar ParseHistorical(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var headings = MeetingHeading().Matches(html);
        if (headings.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no meeting heading");
        }

        var rows = new List<ObservedCatalyst>();
        var rejected = new List<string>();
        var skipped = new List<string>();
        for (var index = 0; index < headings.Count; index++)
        {
            var text = CalendarText.Clean(headings[index].Groups["text"].Value);
            var body = Segment(html, headings[index].Index + headings[index].Length, index + 1 < headings.Count ? headings[index + 1].Index : html.Length);
            var heading = HistoricalHeading().Match(text);
            var outcome = heading.Success
                ? Meeting(
                    int.Parse(heading.Groups["year"].Value, CultureInfo.InvariantCulture),
                    heading.Groups["month"].Value,
                    heading.Groups["days"].Value + (heading.Groups["note"].Success ? $" ({heading.Groups["note"].Value})" : ""),
                    tentative: false,
                    sep: body.Contains("Projection", StringComparison.Ordinal))
                : default;
            switch (outcome)
            {
                case { Skip: { } label }:
                    skipped.Add(label);
                    break;
                case { Row: { } observed }:
                    rows.Add(observed);
                    break;
                default:
                    rejected.Add(CalendarText.Quote(text));
                    break;
            }
        }

        if (rows.Count + skipped.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no meeting heading parses");
        }

        return new ParsedCalendar(rows, rejected, skipped, CoverageFrom: null, CoverageTo: null);
    }

    /// <summary>
    /// One meeting from its month cell (<c>October</c>, <c>Apr/May</c>) and day cell (<c>27-28</c>,
    /// <c>17-18*</c>, <c>30-1</c>, <c>22 (notation vote)</c>). The decision day is the last day, in
    /// the second month when the cell names two. <paramref name="sep"/> overrides the <c>*</c> marker
    /// (historical pages carry none). Default when the cells do not describe a meeting.
    /// </summary>
    private static Outcome Meeting(int year, string monthCell, string dayCell, bool tentative, bool? sep = null)
    {
        var months = MonthPair().Match(monthCell);
        var days = DayCell().Match(dayCell);
        if (!months.Success || !days.Success || CalendarText.Month(months.Groups["first"].Value) is not { } first)
        {
            return default;
        }

        int? second = months.Groups["second"].Success ? CalendarText.Month(months.Groups["second"].Value) : first;
        var firstDay = int.Parse(days.Groups["first"].Value, CultureInfo.InvariantCulture);
        var lastDay = days.Groups["last"].Success ? int.Parse(days.Groups["last"].Value, CultureInfo.InvariantCulture) : firstDay;
        if (second is not { } lastMonth || !(lastMonth == first ? lastDay >= firstDay : lastMonth == first + 1)
            || CalendarText.Date(year, first, firstDay) is null || CalendarText.Date(year, lastMonth, lastDay) is not { } decision)
        {
            return default;
        }

        var status = CatalystStatus.Scheduled;
        if (days.Groups["note"].Success)
        {
            var note = days.Groups["note"].Value.Trim();
            if (note.Equals("notation vote", StringComparison.OrdinalIgnoreCase))
            {
                return new Outcome(null, "notation vote");
            }

            if (note.Equals("unscheduled", StringComparison.OrdinalIgnoreCase))
            {
                return new Outcome(null, "unscheduled meeting");
            }

            if (!note.Equals("cancelled", StringComparison.OrdinalIgnoreCase) && !note.Equals("canceled", StringComparison.OrdinalIgnoreCase))
            {
                return default;
            }

            status = CatalystStatus.Cancelled;
        }

        var dates = lastDay == firstDay && lastMonth == first
            ? string.Create(CultureInfo.InvariantCulture, $"{CalendarText.MonthName(first)} {firstDay}")
            : lastMonth == first
                ? string.Create(CultureInfo.InvariantCulture, $"{CalendarText.MonthName(first)} {firstDay}-{lastDay}")
                : string.Create(CultureInfo.InvariantCulture, $"{CalendarText.MonthName(first)} {firstDay}-{CalendarText.MonthName(lastMonth)} {lastDay}");
        return new Outcome(
            new ObservedCatalyst(
                CatalystFamily.Fomc,
                MarketTime.AtNewYork(decision, DecisionTime),
                TimeAnnounced: true,
                Title: string.Create(CultureInfo.InvariantCulture, $"FOMC meeting {dates}, {year}"),
                ReferencePeriod: null,
                SourceKey: null,
                Sep: sep ?? days.Groups["sep"].Success,
                Tentative: tentative,
                status,
                Derivation: null),
            null);
    }

    /// <summary>A panel's or meeting's text, ending before the page's last-update block when that comes first.</summary>
    private static string Segment(string html, int start, int end)
    {
        var lastUpdate = html.IndexOf("id=\"lastUpdate\"", start, end - start, StringComparison.Ordinal);
        return html[start..(lastUpdate >= 0 ? lastUpdate : end)];
    }

    /// <param name="Row">The meeting, scheduled or cancelled.</param>
    /// <param name="Skip">Why the row is deliberately not recorded.</param>
    private readonly record struct Outcome(ObservedCatalyst? Row, string? Skip);

    [GeneratedRegex(@"<h4[^>]*>\s*(?:<a[^>]*>)?\s*(?<year>\d{4})\s+FOMC\s+Meetings\s*(?:</a>)?\s*</h4>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PanelHeading();

    [GeneratedRegex(@"<div\s+class=""[^""]*\bfomc-meeting__month\b[^""]*""[^>]*>(?<text>.*?)</div>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex MonthCell();

    [GeneratedRegex(@"<div\s+class=""[^""]*\bfomc-meeting__date\b[^""]*""[^>]*>(?<text>.*?)</div>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DateCell();

    [GeneratedRegex(@"<h5[^>]*\bpanel-heading\b[^>]*>(?<text>.*?)</h5>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex MeetingHeading();

    [GeneratedRegex(@"^(?<month>[A-Za-z]+(?:/[A-Za-z]+)?)\s+(?<days>\d{1,2}(?:-\d{1,2})?)(?:\s*\((?<note>[^)]*)\))?(?:\s+Meeting)?\s+-\s+(?<year>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex HistoricalHeading();

    [GeneratedRegex(@"^(?<first>[A-Za-z]+)(?:/(?<second>[A-Za-z]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthPair();

    [GeneratedRegex(@"^(?<first>\d{1,2})(?:\s*-\s*(?<last>\d{1,2}))?(?<sep>\*)?(?:\s*\((?<note>[^)]*)\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex DayCell();
}
