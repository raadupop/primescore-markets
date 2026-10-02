using System.Globalization;
using System.Text;
using PrimeScore.Modules.Ingestion.Sources.Cboe;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>
/// The strict Cboe history parser: layout drift is an error, a bad close on a well-formed row is missing.
/// Every value below is invented in the real Cboe column layout; none is Cboe data.
/// </summary>
public sealed class CboeCsvTests
{
    [Fact]
    public void An_OHLC_file_yields_the_CLOSE_column()
    {
        var file = Parse("VIX", "DATE,OPEN,HIGH,LOW,CLOSE\n09/24/2026,21.100000,22.200000,20.300000,21.450000\n");

        Assert.Equal([new CboeClose(new DateOnly(2026, 9, 24), 21.45)], file.Closes);
        Assert.Empty(file.Missing);
    }

    [Fact]
    public void A_single_column_file_yields_the_column_named_like_the_symbol()
    {
        var file = Parse("VVIX", "DATE,VVIX\n09/28/2026,101.330000\n");

        Assert.Equal([new CboeClose(new DateOnly(2026, 9, 28), 101.33)], file.Closes);
    }

    [Fact]
    public void A_byte_order_mark_carriage_returns_and_blank_lines_are_tolerated()
    {
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("DATE,VVIX\r\n09/25/2026,100.5\r\n\r\n09/28/2026,101.33\r\n\r\n")).ToArray();

        var file = CboeCsv.Parse("VVIX", body);

        Assert.Equal([100.5, 101.33], file.Closes.Select(close => close.Close));
    }

    [Fact]
    public void Unusable_closes_on_well_formed_rows_are_missing_dates_never_values()
    {
        var file = Parse("VVIX", "DATE,VVIX\n09/21/2026,\n09/22/2026,.\n09/23/2026,NaN\n09/24/2026,0\n09/25/2026,-1\n");

        Assert.Empty(file.Closes);
        Assert.Equal(5, file.Missing.Count);
        Assert.Equal(new DateOnly(2026, 9, 21), file.Missing[0]);
    }

    [Fact]
    public void Rows_come_back_in_date_order_whatever_the_file_order()
    {
        var file = Parse("VVIX", "DATE,VVIX\n09/28/2026,101\n09/24/2026,99\n09/25/2026,100\n");

        Assert.Equal([24, 25, 28], file.Closes.Select(close => close.Date.Day));
    }

    [Theory]
    [InlineData("DATE,OPEN,HIGH,LOW,CLOSE\n2026-09-24,1,2,3,4\n", "VIX_History.csv: row 2 malformed")]
    [InlineData("DATE,OPEN,HIGH,LOW,CLOSE\n09/24/2026,1,2,3,4,5\n", "VIX_History.csv: row 2 malformed")]
    [InlineData("DATE,OPEN,HIGH,LOW,CLOSE\n09/24/2026,1,2,3,4\n09/24/2026,1,2,3,5\n", "VIX_History.csv: row 3 malformed")]
    [InlineData("DATE,OPEN,HIGH,LOW\n09/24/2026,1,2,3\n", "VIX_History.csv: unexpected header")]
    [InlineData("", "VIX_History.csv: unexpected header")]
    public void Layout_drift_fails_the_whole_file(string body, string expected)
    {
        var failure = Assert.Throws<CboeFormatException>(() => Parse("VIX", body));

        Assert.StartsWith(expected, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_challenge_page_served_with_200_fails_without_echoing_the_body()
    {
        var failure = Assert.Throws<CboeFormatException>(() =>
            Parse("VIX", "<!DOCTYPE html><html><head><title>Just a moment...</title></head><body>challenge</body></html>"));

        Assert.DoesNotContain("<", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("html", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VIX_History.csv", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parsing_ignores_the_current_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var file = Parse("VIX", "DATE,OPEN,HIGH,LOW,CLOSE\n09/24/2026,21.100000,22.200000,20.300000,21.450000\n");

            Assert.Equal(21.45, Assert.Single(file.Closes).Close);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static CboeFile Parse(string symbol, string body) => CboeCsv.Parse(symbol, Encoding.UTF8.GetBytes(body));
}
