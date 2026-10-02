using System.Globalization;
using System.Text.RegularExpressions;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Eia;

/// <summary>
/// Parses EIA's WPSR schedule page (ADR-0011). EIA states the standard release (Wednesday 10:30
/// New York, five days after the Friday that ends the data week) and lists the exceptions in a
/// table keyed by that Friday. One row per data week is produced from the table's first to its last
/// listed week: the table's date and time where it has a row, otherwise the standard Wednesday with
/// derivation <c>standard-weekday</c> (EIA asserts those weeks by listing the exceptions through
/// that point). Nothing is produced past the last listed week, where a later exception would
/// otherwise read as a reschedule. A table row that does not read is quoted, and its week (when
/// that cell reads) gets no row, since the standard day would be a guess. Rows are keyed by the
/// week-ending Friday (<c>WPSR:2026-09-25</c>).
/// </summary>
internal static partial class WpsrSchedulePage
{
    public const string Title = "Weekly Petroleum Status Report";

    public const string StandardWeekday = "standard-weekday";

    /// <summary>Wednesday after the week-ending Friday.</summary>
    public const int StandardDaysAfterWeekEnd = 5;

    public static readonly TimeOnly StandardTime = new(10, 30);

    /// <summary>Coverage runs from the first to the last release date produced.</summary>
    public static ParsedCalendar Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var table = Tables().Matches(html).FirstOrDefault(match =>
        {
            var header = CalendarText.Clean(HeaderRow().Match(match.Groups["body"].Value).Value);
            return header.Contains("week ending", StringComparison.OrdinalIgnoreCase) && header.Contains("release date", StringComparison.OrdinalIgnoreCase);
        }) ?? throw CalendarText.LayoutNotRecognised("no schedule table with week ending and release date columns");

        var exceptions = new Dictionary<DateOnly, DateTimeOffset>();
        var unread = new HashSet<DateOnly>();
        var rejected = new List<string>();
        foreach (Match row in Rows().Matches(table.Groups["body"].Value))
        {
            var cells = DataCells().Matches(row.Groups["row"].Value).Select(cell => CalendarText.Clean(cell.Groups["cell"].Value)).ToArray();
            if (cells.Length == 0)
            {
                continue; // the header row has column headings only
            }

            var week = LongDate(CalendarText.Clean(RowHeading().Match(row.Groups["row"].Value).Groups["cell"].Value));
            var release = cells.Length >= 3 ? LongDate(cells[0]) : null;
            if (week is not { DayOfWeek: DayOfWeek.Friday } weekEnding || release is not { } date || exceptions.ContainsKey(weekEnding)
                || !string.Equals(cells[1], date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)
                || ReleaseTime(cells[2]) is not { } time)
            {
                rejected.Add(CalendarText.Quote(row.Value));
                if (week is { DayOfWeek: DayOfWeek.Friday } listedWeek)
                {
                    unread.Add(listedWeek);
                }

                continue;
            }

            exceptions[weekEnding] = MarketTime.AtNewYork(date, time);
        }

        if (exceptions.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no exception row reads");
        }

        var rows = new List<ObservedCatalyst>();
        var weeks = exceptions.Keys.Concat(unread).ToArray();
        for (var weekEnding = weeks.Min(); weekEnding <= weeks.Max(); weekEnding = weekEnding.AddDays(7))
        {
            if (unread.Contains(weekEnding) && !exceptions.ContainsKey(weekEnding))
            {
                continue;
            }

            var listed = exceptions.TryGetValue(weekEnding, out var at);
            rows.Add(new ObservedCatalyst(
                CatalystFamily.Wpsr,
                listed ? at : MarketTime.AtNewYork(weekEnding.AddDays(StandardDaysAfterWeekEnd), StandardTime),
                true,
                Title,
                "week ending " + weekEnding.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "WPSR:" + weekEnding.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                null,
                false,
                CatalystStatus.Scheduled,
                listed ? null : StandardWeekday));
        }

        var dates = rows.Select(row => MarketTime.NewYorkDate(row.ScheduledAt)).ToArray();
        return new ParsedCalendar(rows, rejected, [], dates.Min(), dates.Max());
    }

    /// <summary><c>December 27, 2024</c>.</summary>
    private static DateOnly? LongDate(string text) =>
        LongDateText().Match(text) is { Success: true } match && CalendarText.Month(match.Groups["month"].Value) is { } month
            ? CalendarText.Date(
                int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture), month, int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture))
            : null;

    /// <summary><c>11:00 a.m.</c>, <c>12:00 p.m.</c>, <c>5:00 p.m.</c>.</summary>
    private static TimeOnly? ReleaseTime(string text)
    {
        var normalized = text.Replace("a.m.", "AM", StringComparison.OrdinalIgnoreCase).Replace("p.m.", "PM", StringComparison.OrdinalIgnoreCase);
        return DateTime.TryParseExact(normalized, ["h:mm tt", "hh:mm tt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? TimeOnly.FromDateTime(parsed)
            : null;
    }

    [GeneratedRegex(@"<table[^>]*>(?<body>.*?)</table>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Tables();

    [GeneratedRegex(@"<tr[^>]*>(?:(?!</tr>).)*<th[^>]*\bscope=""col""[^>]*>.*?</tr>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex HeaderRow();

    [GeneratedRegex(@"<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Rows();

    [GeneratedRegex(@"<th[^>]*>(?<cell>.*?)</th>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex RowHeading();

    [GeneratedRegex(@"<td[^>]*>(?<cell>.*?)</td>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DataCells();

    [GeneratedRegex(@"^(?<month>[A-Za-z]+)\.?\s+(?<day>\d{1,2}),\s*(?<year>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex LongDateText();
}
