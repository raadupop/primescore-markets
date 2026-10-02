using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using PrimeScore.Modules.Catalysts.Recording;

namespace PrimeScore.Modules.Catalysts.Sources;

/// <summary>What a calendar parser read from one page.</summary>
/// <param name="Rows">Scheduled and cancelled rows, and placeholders (a key listed without a date).</param>
/// <param name="Rejected">Rows of the expected shape that did not parse, as quoted text; reported as item errors.</param>
/// <param name="Skipped">One label per row deliberately not recorded (<c>notation vote</c>, <c>unscheduled meeting</c>).</param>
/// <param name="CoverageFrom">First New York date the page lists completely; null when it makes no such claim (archives).</param>
internal sealed record ParsedCalendar(
    IReadOnlyList<ObservedCatalyst> Rows,
    IReadOnlyList<string> Rejected,
    IReadOnlyList<string> Skipped,
    DateOnly? CoverageFrom,
    DateOnly? CoverageTo)
{
    public int RowsExamined => Rows.Count + Rejected.Count + Skipped.Count;
}

/// <summary>Text helpers shared by the HTML calendar parsers.</summary>
internal static partial class CalendarText
{
    /// <summary>The parser found none of its expected structure, or the structure but no row that parses.</summary>
    public static InvalidOperationException LayoutNotRecognised(string what) => new($"layout not recognised: {what}");

    /// <summary>Markup removed, entities decoded, whitespace collapsed (a non-breaking space counts as whitespace).</summary>
    public static string Clean(string html) =>
        Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    /// <summary>A rejected row as it reads on the page, at most 200 characters.</summary>
    public static string Quote(string html)
    {
        var text = Clean(html);
        return text.Length <= 200 ? text : text[..197] + "...";
    }

    /// <summary>1–12 for a full English month name, a three-letter abbreviation or <c>Sept</c> (any case, trailing dot allowed); else null.</summary>
    public static int? Month(string name)
    {
        var text = name.Trim().TrimEnd('.');
        for (var month = 1; month <= 12; month++)
        {
            var full = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
            if (string.Equals(text, full, StringComparison.OrdinalIgnoreCase)
                || (text.Length == 3 && full.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                || (month == 9 && string.Equals(text, "Sept", StringComparison.OrdinalIgnoreCase)))
            {
                return month;
            }
        }

        return null;
    }

    /// <summary>The date, or null when the day does not exist in that month.</summary>
    public static DateOnly? Date(int year, int month, int day) =>
        year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;

    public static string MonthName(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
