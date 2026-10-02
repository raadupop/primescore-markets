using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources;

/// <param name="Rows">Empty whenever <paramref name="Errors"/> is not.</param>
/// <param name="Errors">One entry per problem, each naming its line; the file is valid only when empty.</param>
internal sealed record CatalystCsvFile(IReadOnlyList<ObservedCatalyst> Rows, IReadOnlyList<string> Errors);

/// <summary>
/// Operator-curated catalyst rows (the OPEC calendar and the back-fill import), one per line under
/// the exact header <see cref="Header"/>. Every row names the URL it was checked against, when that
/// URL was read (with an offset, not after the recording time) and who checked it, so a curated date
/// carries the same provenance as a fetched one. Validation is whole-file: one bad row rejects the
/// file and every problem is listed with its line number. Culture-invariant; RFC 4180 quoting within
/// a line.
/// </summary>
internal static partial class CatalystCsv
{
    public const string Derivation = "curated";

    public static readonly string[] Header =
    [
        "family", "scheduled_date", "scheduled_time_new_york", "originally_scheduled_date", "title", "reference_period",
        "source_key", "sep", "source_url", "retrieved_at", "verified_by", "note",
    ];

    /// <param name="recordingTime">The recording clock: a row retrieved after it cannot have been checked yet.</param>
    public static CatalystCsvFile Parse(string text, DateTimeOffset recordingTime)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.TrimStart('﻿').Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var errors = new List<string>();
        var headerLine = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (headerLine < 0 || !Split(lines[headerLine]).Select(cell => cell.Trim()).SequenceEqual(Header, StringComparer.Ordinal))
        {
            return new CatalystCsvFile([], [$"line {Math.Max(headerLine, 0) + 1}: header must be: {string.Join(",", Header)}"]);
        }

        var rows = new List<ObservedCatalyst>();
        for (var index = headerLine + 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var problems = new List<string>();
            var row = ParseRow(Split(lines[index]).Select(cell => cell.Trim()).ToArray(), recordingTime, problems);
            errors.AddRange(problems.Select(problem => string.Create(CultureInfo.InvariantCulture, $"line {index + 1}: {problem}")));
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return errors.Count > 0 ? new CatalystCsvFile([], errors) : new CatalystCsvFile(rows, []);
    }

    private static ObservedCatalyst? ParseRow(string[] cells, DateTimeOffset recordingTime, List<string> problems)
    {
        if (cells.Length != Header.Length)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"expected {Header.Length} columns, found {cells.Length}"));
            return null;
        }

        if (!CatalystFamilyNames.TryParse(cells[0], out var family))
        {
            problems.Add($"family must be one of {string.Join(", ", Enum.GetValues<CatalystFamily>().Select(value => value.ToWireName()))}");
        }

        if (!TryDate(cells[1], out var scheduledDate))
        {
            problems.Add("scheduled_date must be YYYY-MM-DD");
        }

        TimeOnly? time = null;
        if (cells[2].Length > 0)
        {
            if (TimeOnly.TryParseExact(cells[2], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                time = parsed;
            }
            else
            {
                problems.Add("scheduled_time_new_york must be HH:mm or empty");
            }
        }

        DateOnly? original = null;
        if (cells[3].Length > 0)
        {
            if (!TryDate(cells[3], out var parsed))
            {
                problems.Add("originally_scheduled_date must be YYYY-MM-DD or empty");
            }
            else if (parsed == scheduledDate)
            {
                problems.Add("originally_scheduled_date must differ from scheduled_date (leave it empty when the release did not move)");
            }
            else
            {
                original = parsed;
            }
        }

        bool? sep = null;
        if (cells[7].Length > 0)
        {
            if (!bool.TryParse(cells[7], out var parsed))
            {
                problems.Add("sep must be true, false or empty");
            }
            else if (family != CatalystFamily.Fomc)
            {
                problems.Add("sep applies to FOMC rows only");
            }
            else
            {
                sep = parsed;
            }
        }

        if (!Uri.TryCreate(cells[8], UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            problems.Add("source_url must be an absolute http(s) URL");
        }

        var retrieved = DateTimeOffset.MinValue;
        if (!IsoWithOffset().IsMatch(cells[9])
            || !DateTimeOffset.TryParse(cells[9], CultureInfo.InvariantCulture, DateTimeStyles.None, out retrieved))
        {
            problems.Add("retrieved_at must be an ISO 8601 date-time with an offset (e.g. 2026-09-01T12:00:00Z)");
        }
        else if (retrieved > recordingTime)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"retrieved_at {retrieved.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'} is later than the recording time {recordingTime.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}"));
        }

        if (cells[10].Length == 0)
        {
            problems.Add("verified_by is required");
        }

        if (problems.Count > 0)
        {
            return null;
        }

        return new ObservedCatalyst(
            family,
            MarketTime.AtNewYork(scheduledDate, time ?? TimeOnly.MinValue),
            time is not null,
            cells[4].Length > 0 ? cells[4] : family.ToWireName(),
            cells[5].Length > 0 ? cells[5] : null,
            cells[6].Length > 0 ? cells[6] : null,
            sep,
            Tentative: false,
            CatalystStatus.Scheduled,
            Derivation,
            RowSourceUrl: cells[8],
            RowRetrievedAt: retrieved.ToUniversalTime(),
            OriginallyScheduledDate: original,
            VerifiedBy: cells[10]);
    }

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoWithOffset();

    /// <summary>Comma-separated cells with double-quoted fields (RFC 4180 subset, one line).</summary>
    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }
}
