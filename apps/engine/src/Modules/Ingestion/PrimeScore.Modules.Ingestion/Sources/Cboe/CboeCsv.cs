using System.Globalization;
using System.Text;

namespace PrimeScore.Modules.Ingestion.Sources.Cboe;

internal readonly record struct CboeClose(DateOnly Date, double Close);

/// <param name="Closes">Ascending by date, one per date.</param>
/// <param name="Missing">Dates of well-formed rows without a usable close (empty, ".", non-finite or not positive); never filled.</param>
internal sealed record CboeFile(IReadOnlyList<CboeClose> Closes, IReadOnlyList<DateOnly> Missing);

/// <summary>A history file that does not have the expected layout. The message never echoes the body.</summary>
internal sealed class CboeFormatException(string message) : Exception(message);

/// <summary>
/// Strict parser of Cboe daily index history files (<c>DATE,OPEN,HIGH,LOW,CLOSE</c> or
/// <c>DATE,&lt;SYMBOL&gt;</c>; dates <c>MM/dd/yyyy</c>). Any drift in layout (header, column count,
/// date format, a repeated date) fails the whole file rather than turning into missing values, so
/// a Cloudflare challenge page served with 200 or a changed format is an error the operator sees.
/// Tolerates a BOM, CRLF and blank lines, and does not rely on row order.
/// </summary>
internal static class CboeCsv
{
    public static CboeFile Parse(string symbol, ReadOnlySpan<byte> body)
    {
        var file = $"{symbol}_History.csv";
        var lines = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(body).TrimStart((char)0xFEFF).Split('\n');
        var headerLine = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        var header = headerLine < 0 ? [] : Columns(lines[headerLine]).Select(column => column.ToUpperInvariant()).ToArray();
        var closeColumn = Array.IndexOf(header, "CLOSE");
        if (closeColumn < 0)
        {
            closeColumn = Array.IndexOf(header, symbol.ToUpperInvariant());
        }

        if (header.Length < 2 || header[0] != "DATE" || closeColumn < 1)
        {
            throw new CboeFormatException($"{file}: unexpected header; expected DATE then CLOSE or {symbol}");
        }

        var closes = new Dictionary<DateOnly, double>();
        var missing = new List<DateOnly>();
        for (var index = headerLine + 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var columns = Columns(lines[index]);
            if (columns.Length != header.Length
                || !DateOnly.TryParseExact(columns[0], "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || closes.ContainsKey(date) || missing.Contains(date))
            {
                throw new CboeFormatException($"{file}: row {index + 1} malformed");
            }

            if (double.TryParse(columns[closeColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var close)
                && double.IsFinite(close) && close > 0)
            {
                closes[date] = close;
            }
            else
            {
                missing.Add(date);
            }
        }

        return new CboeFile(
            closes.OrderBy(pair => pair.Key).Select(pair => new CboeClose(pair.Key, pair.Value)).ToArray(),
            [.. missing.Order()]);
    }

    private static string[] Columns(string line) =>
        line.TrimEnd('\r').Split(',').Select(column => column.Trim()).ToArray();
}
