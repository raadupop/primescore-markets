using System.Globalization;
using System.Text.RegularExpressions;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Bls;

/// <summary>
/// Parses BLS release schedules (ADR-0011): a per-release page (<c>cpi.htm</c>, <c>empsit.htm</c>;
/// one row per reference month with release date and time) and an archived year page (one table
/// per month listing every release). Rows are keyed by family and reference month
/// (<c>CPI:2026-09</c>), so a release that moves is still the same catalyst. A per-release row
/// whose date is blank or "TBD" is a placeholder: the key is listed without a date. The archives
/// show the dates releases actually happened.
/// </summary>
internal static partial class BlsSchedulePages
{
    public const string CpiTitle = "Consumer Price Index";

    public const string NfpTitle = "Employment Situation";

    /// <summary>A per-release page; coverage runs from its first to its last dated row.</summary>
    public static ParsedCalendar ParseRelease(string html, CatalystFamily family)
    {
        ArgumentNullException.ThrowIfNull(html);
        var title = family switch
        {
            CatalystFamily.Cpi => CpiTitle,
            CatalystFamily.Nfp => NfpTitle,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "BLS schedules list CPI and NFP only."),
        };
        var table = Tables().Matches(html).FirstOrDefault(match =>
        {
            var header = CalendarText.Clean(HeaderRow().Match(match.Groups["body"].Value).Value);
            return header.Contains("Reference Month", StringComparison.OrdinalIgnoreCase) && header.Contains("Release Date", StringComparison.OrdinalIgnoreCase);
        }) ?? throw CalendarText.LayoutNotRecognised("no release-list table with Reference Month and Release Date columns");

        var rows = new List<ObservedCatalyst>();
        var rejected = new List<string>();
        foreach (Match row in Rows().Matches(table.Groups["body"].Value))
        {
            var cells = Cells().Matches(row.Groups["row"].Value).Select(cell => CalendarText.Clean(cell.Groups["cell"].Value)).ToArray();
            if (cells.Length == 0)
            {
                continue; // the header row has th cells only
            }

            if (cells.Length != 3 || ReferenceMonth(cells[0]) is not { } reference)
            {
                rejected.Add(CalendarText.Quote(row.Value));
                continue;
            }

            var key = Key(family, reference);
            var period = Period(reference);
            if (IsPlaceholder(cells[1]))
            {
                rows.Add(new ObservedCatalyst(family, default, false, title, period, key, null, false, CatalystStatus.Scheduled, null, Placeholder: cells[1]));
                continue;
            }

            if (ReleaseDate(cells[1]) is not { } date || !TryReleaseTime(cells[2], out var time))
            {
                rejected.Add(CalendarText.Quote(row.Value));
                continue;
            }

            rows.Add(Release(family, title, period, key, date, time));
        }

        var dates = rows.Where(row => row.Placeholder is null).Select(row => MarketTime.NewYorkDate(row.ScheduledAt)).ToArray();
        if (rows.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no release row parses");
        }

        return new ParsedCalendar(rows, rejected, [], dates.Length == 0 ? null : dates.Min(), dates.Length == 0 ? null : dates.Max());
    }

    /// <summary>An archived year page: its Consumer Price Index and Employment Situation rows (kind Archive); no coverage claim.</summary>
    public static ParsedCalendar ParseArchive(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var tables = Tables().Matches(html);
        if (tables.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no release-list table");
        }

        var rows = new List<ObservedCatalyst>();
        var rejected = new List<string>();
        foreach (Match table in tables)
        {
            foreach (Match row in Rows().Matches(table.Groups["body"].Value))
            {
                var description = DescriptionCell().Match(row.Value);
                if (!description.Success)
                {
                    continue;
                }

                var release = CalendarText.Clean(description.Groups["release"].Value);
                CatalystFamily? family = release switch
                {
                    CpiTitle => CatalystFamily.Cpi,
                    NfpTitle => CatalystFamily.Nfp,
                    _ => null,
                };
                if (family is not { } kept)
                {
                    continue; // other releases, holidays, "Employment Situation of Veterans"
                }

                var forText = ForPeriod().Match(CalendarText.Clean(description.Groups["rest"].Value));
                if (!forText.Success || ReferenceMonth(forText.Groups["period"].Value) is not { } reference
                    || ArchiveDate(CalendarText.Clean(DateCell().Match(row.Value).Groups["text"].Value)) is not { } date
                    || !TryReleaseTime(CalendarText.Clean(TimeCell().Match(row.Value).Groups["text"].Value), out var time))
                {
                    rejected.Add(CalendarText.Quote(row.Value));
                    continue;
                }

                rows.Add(Release(kept, release, Period(reference), Key(kept, reference), date, time));
            }
        }

        if (rows.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no Consumer Price Index or Employment Situation row parses");
        }

        return new ParsedCalendar(rows, rejected, [], CoverageFrom: null, CoverageTo: null);
    }

    private static ObservedCatalyst Release(CatalystFamily family, string title, string period, string key, DateOnly date, TimeOnly? time) =>
        new(family, MarketTime.AtNewYork(date, time ?? TimeOnly.MinValue), time is not null, title, period, key, null, false, CatalystStatus.Scheduled, null);

    /// <summary><c>CPI:2026-09</c>: family wire name and reference month.</summary>
    private static string Key(CatalystFamily family, DateOnly reference) =>
        family.ToWireName() + ":" + reference.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Period(DateOnly reference) =>
        string.Create(CultureInfo.InvariantCulture, $"{CalendarText.MonthName(reference.Month)} {reference.Year}");

    private static bool IsPlaceholder(string cell) =>
        cell.Length == 0
        || cell.Equals("TBD", StringComparison.OrdinalIgnoreCase)
        || cell.Equals("to be announced", StringComparison.OrdinalIgnoreCase)
        || cell.Equals("to be determined", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first day of <c>September 2026</c>.</summary>
    private static DateOnly? ReferenceMonth(string text) =>
        MonthYear().Match(text) is { Success: true } match && CalendarText.Month(match.Groups["month"].Value) is { } month
            ? CalendarText.Date(int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture), month, 1)
            : null;

    /// <summary><c>Oct. 14, 2026</c>, <c>May 12, 2026</c>, <c>Jan. 09, 2026</c>, <c>Sept. 11, 2026</c>, <c>October 14, 2026</c>.</summary>
    private static DateOnly? ReleaseDate(string text) =>
        MonthDayYear().Match(text) is { Success: true } match && CalendarText.Month(match.Groups["month"].Value) is { } month
            ? CalendarText.Date(
                int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture), month, int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture))
            : null;

    /// <summary><c>Thursday, January 16, 2014</c> or <c>Friday, August 1, 2025</c>; the weekday must agree with the date.</summary>
    private static DateOnly? ArchiveDate(string text)
    {
        var comma = text.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0 || ReleaseDate(text[(comma + 1)..].Trim()) is not { } date)
        {
            return null;
        }

        return string.Equals(text[..comma].Trim(), date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase) ? date : null;
    }

    /// <summary><c>08:30 AM</c>; a blank or "TBD" cell parses as no time announced (null).</summary>
    private static bool TryReleaseTime(string text, out TimeOnly? time)
    {
        time = null;
        if (IsPlaceholder(text))
        {
            return true;
        }

        if (!DateTime.TryParseExact(text, ["hh:mm tt", "h:mm tt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        time = TimeOnly.FromDateTime(parsed);
        return true;
    }

    [GeneratedRegex(@"<table[^>]*\bclass=""[^""]*\brelease-list\b[^""]*""[^>]*>(?<body>.*?)</table>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Tables();

    [GeneratedRegex(@"<tr[^>]*>(?:(?!</tr>).)*<th[^>]*>.*?</tr>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex HeaderRow();

    [GeneratedRegex(@"<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Rows();

    [GeneratedRegex(@"<td[^>]*>(?<cell>.*?)</td>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Cells();

    [GeneratedRegex(@"<td[^>]*\bclass=""[^""]*\bdesc-cell\b[^""]*""[^>]*>.*?<strong>(?<release>.*?)</strong>(?<rest>.*?)</td>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DescriptionCell();

    [GeneratedRegex(@"<td[^>]*\bclass=""[^""]*\bdate-cell\b[^""]*""[^>]*>(?<text>.*?)</td>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DateCell();

    [GeneratedRegex(@"<td[^>]*\bclass=""[^""]*\btime-cell\b[^""]*""[^>]*>(?<text>.*?)</td>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TimeCell();

    [GeneratedRegex(@"^for\s+(?<period>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ForPeriod();

    [GeneratedRegex(@"^(?<month>[A-Za-z]+)\.?\s+(?<year>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthYear();

    [GeneratedRegex(@"^(?<month>[A-Za-z]+)\.?\s+(?<day>\d{1,2}),\s*(?<year>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthDayYear();
}
