using System.Globalization;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Classification.Contracts;

namespace PrimeScore.Modules.Classification.Consensus;

/// <summary>Bound from <c>Consensus</c>.</summary>
internal sealed class ConsensusOptions
{
    public const string Section = "Consensus";

    /// <summary>Directory of operator-curated <c>&lt;INDICATOR&gt;.csv</c> files; the host defaults it to <c>apps/engine/data/consensus</c>.</summary>
    public string? Directory { get; set; }
}

/// <param name="ReleaseDate">New York calendar date of the release the consensus refers to.</param>
internal sealed record ConsensusRow(DateOnly ReleaseDate, double Consensus, string Source, string Url, DateTimeOffset RetrievedAt, string? ActualSource)
{
    public ConsensusUsed ToUsed() => new(Consensus, Source, Url, RetrievedAt);
}

/// <param name="Error">Why the file could not be read this time; the last good rows stay in use.</param>
internal sealed record ConsensusFileStatus(string Indicator, string Path, int Rows, IReadOnlyList<string> RejectedRows, string? Error = null);

/// <summary>
/// Operator-curated consensus expectations (brief §7), one CSV per indicator with columns
/// <c>release_date, actual_source, consensus, consensus_source, consensus_url, retrieved_at</c>.
/// A row without a source, a URL or a retrieval time is rejected: a macro print is never
/// classified against an expectation whose origin cannot be shown.
/// </summary>
internal sealed class ConsensusBook(IOptions<ConsensusOptions> options)
{
    private static readonly string[] Header = ["release_date", "actual_source", "consensus", "consensus_source", "consensus_url", "retrieved_at"];

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTime Stamp, Dictionary<DateOnly, ConsensusRow> Rows, ConsensusFileStatus Status)> _cache = new(StringComparer.Ordinal);

    public string? Directory => options.Value.Directory;

    public ConsensusRow? Find(string indicator, DateOnly releaseDate) =>
        Load(indicator)?.Rows.GetValueOrDefault(releaseDate);

    public IReadOnlyList<ConsensusFileStatus> Status()
    {
        if (string.IsNullOrWhiteSpace(Directory) || !System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(Directory, "*.csv")
            .Select(path => Load(Path.GetFileNameWithoutExtension(path))?.Status)
            .OfType<ConsensusFileStatus>()
            .OrderBy(status => status.Indicator, StringComparer.Ordinal)
            .ToArray();
    }

    private (DateTime Stamp, Dictionary<DateOnly, ConsensusRow> Rows, ConsensusFileStatus Status)? Load(string indicator)
    {
        // Instruments of unknown macro sources are caller text: only symbol-shaped names can name a file.
        if (string.IsNullOrWhiteSpace(Directory) || indicator.Length is 0 or > 64 || !indicator.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            return null;
        }

        var path = Path.Combine(Directory, indicator + ".csv");
        if (!File.Exists(path))
        {
            return null;
        }

        lock (_gate)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (_cache.TryGetValue(indicator, out var cached) && cached.Stamp == stamp)
                {
                    return cached;
                }

                // Shared read: a file open in a spreadsheet program must not stop classification.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                while (reader.ReadLine() is { } line)
                {
                    lines.Add(line);
                }

                var loaded = Parse(indicator, path, lines);
                _cache[indicator] = (stamp, loaded.Rows, loaded.Status);
                return _cache[indicator];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var error = $"could not be read ({exception.Message}); the last good rows stay in use";
                if (_cache.TryGetValue(indicator, out var previous))
                {
                    return previous with { Status = previous.Status with { Error = error } };
                }

                return (DateTime.MinValue, new Dictionary<DateOnly, ConsensusRow>(), new ConsensusFileStatus(indicator, path, 0, [], error));
            }
        }
    }

    internal static (Dictionary<DateOnly, ConsensusRow> Rows, ConsensusFileStatus Status) Parse(string indicator, string path, IReadOnlyList<string> lines)
    {
        var rows = new Dictionary<DateOnly, ConsensusRow>();
        var rejected = new List<string>();
        var header = lines.Count > 0 ? Split(lines[0]).Select(cell => cell.Trim()).ToArray() : [];
        if (!header.SequenceEqual(Header, StringComparer.Ordinal))
        {
            rejected.Add($"header must be: {string.Join(",", Header)}");
            return (rows, new ConsensusFileStatus(indicator, path, 0, rejected));
        }

        for (var index = 1; index < lines.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var cells = Split(lines[index]);
            var problem = RowProblem(cells, out var row);
            if (problem is not null)
            {
                rejected.Add($"line {index + 1}: {problem}");
            }
            else if (!rows.TryAdd(row!.ReleaseDate, row))
            {
                rejected.Add($"line {index + 1}: duplicate release_date {row.ReleaseDate:yyyy-MM-dd}");
            }
        }

        return (rows, new ConsensusFileStatus(indicator, path, rows.Count, rejected));
    }

    private static string? RowProblem(IReadOnlyList<string> cells, out ConsensusRow? row)
    {
        row = null;
        if (cells.Count != Header.Length)
        {
            return $"expected {Header.Length} columns, found {cells.Count}";
        }

        if (!DateOnly.TryParseExact(cells[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var release))
        {
            return "release_date must be YYYY-MM-DD";
        }

        if (!double.TryParse(cells[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var consensus) || !double.IsFinite(consensus))
        {
            return "consensus must be a number";
        }

        var source = cells[3].Trim();
        var url = cells[4].Trim();
        if (source.Length == 0)
        {
            return "consensus_source is required";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return "consensus_url must be an absolute http(s) URL";
        }

        if (!DateTimeOffset.TryParse(cells[5].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retrieved))
        {
            return "retrieved_at must be an ISO 8601 date-time";
        }

        var actualSource = cells[1].Trim();
        row = new ConsensusRow(release, consensus, source, url, retrieved.ToUniversalTime(), actualSource.Length == 0 ? null : actualSource);
        return null;
    }

    /// <summary>Comma-separated cells with double-quoted fields (RFC 4180 subset).</summary>
    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
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
