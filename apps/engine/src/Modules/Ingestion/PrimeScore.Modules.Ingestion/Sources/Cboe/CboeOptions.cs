using System.Globalization;

namespace PrimeScore.Modules.Ingestion.Sources.Cboe;

/// <summary>
/// Bound from <c>Sources:Cboe</c>; disabled unless <c>Enabled</c> is true. Cboe's daily index
/// history files need no key. Invalid values keep the adapter disabled with a reason naming the key.
/// </summary>
internal sealed class CboeOptions
{
    public const string Section = "Sources:Cboe";

    public const string SourceName = "Cboe";

    public const string HttpClientName = "cboe";

    public bool Enabled { get; set; }

    /// <summary>Directory of the <c>&lt;SYMBOL&gt;_History.csv</c> files.</summary>
    public string BaseUrl { get; set; } = "https://cdn.cboe.com/api/global/us_indices/daily_prices/";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Polling starts at this New York time; Cboe updates files from about 18:00 ET.</summary>
    public TimeOnly WindowStartNewYork { get; set; } = new(18, 0);

    /// <summary>Polling stops before this New York time; an end at or before the start is on the next day.</summary>
    public TimeOnly WindowEndNewYork { get; set; } = new(8, 0);

    /// <summary>
    /// Null means <see cref="CboeIndexCatalog.DefaultSymbols"/>. An array that starts null is bound
    /// fresh from configuration, so configured symbols replace the defaults instead of appending to them.
    /// </summary>
    public string[]? Symbols { get; set; }

    public bool RunOnStartup { get; set; }

    /// <summary>Already-recorded closes re-sent with each new date, so a revised recent value is counted.</summary>
    public int OverlapRows { get; set; } = 10;

    /// <summary>Base of the quadratic retry back-off: waits of 1×, 4× and 9× this many seconds.</summary>
    public double RetryDelaySeconds { get; set; } = 5;

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Configured symbols in upper case, without duplicates.</summary>
    public IReadOnlyList<string> SymbolList =>
        (Symbols ?? [.. CboeIndexCatalog.DefaultSymbols])
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The symbol's file, or null when <see cref="BaseUrl"/> is not an absolute http(s) URL: the status
    /// page lists series of a misconfigured adapter too, and must show <see cref="DisabledReason"/>, not fail.
    /// </summary>
    public Uri? TryFileUrl(string symbol) =>
        BaseDirectory() is { } directory ? new Uri(directory, $"{symbol}_History.csv") : null;

    /// <summary>The symbol's file; only called once <see cref="DisabledReason"/> has passed.</summary>
    public Uri FileUrl(string symbol) =>
        TryFileUrl(symbol) ?? throw new InvalidOperationException("Sources:Cboe:BaseUrl must be an absolute http(s) URL");

    public string? DisabledReason()
    {
        if (!Enabled)
        {
            return "disabled by configuration (set Sources:Cboe:Enabled=true)";
        }

        if (BaseDirectory() is null)
        {
            return "Sources:Cboe:BaseUrl must be an absolute http(s) URL";
        }

        var symbols = SymbolList;
        if (symbols.Count == 0)
        {
            return "Sources:Cboe:Symbols is empty";
        }

        if (symbols.FirstOrDefault(symbol => CboeIndexCatalog.Find(symbol) is null) is { } unknown)
        {
            return $"unknown Cboe symbol '{unknown}' in Sources:Cboe:Symbols (supported: {string.Join(", ", CboeIndexCatalog.DefaultSymbols)})";
        }

        if (WindowStartNewYork == WindowEndNewYork)
        {
            return "Sources:Cboe:WindowStartNewYork and WindowEndNewYork must differ";
        }

        var window = WindowEndNewYork > WindowStartNewYork
            ? WindowEndNewYork - WindowStartNewYork
            : TimeSpan.FromDays(1) - (WindowStartNewYork - WindowEndNewYork);
        if (PollInterval <= TimeSpan.Zero || PollInterval > window)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Sources:Cboe:PollInterval must be positive and no longer than the polling window ({window.TotalHours} h)");
        }

        return OverlapRows < 0 ? "Sources:Cboe:OverlapRows must not be negative"
            : TimeoutSeconds <= 0 ? "Sources:Cboe:TimeoutSeconds must be positive"
            : RetryDelaySeconds < 0 ? "Sources:Cboe:RetryDelaySeconds must not be negative"
            : null;
    }

    private Uri? BaseDirectory() =>
        Uri.TryCreate(BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/", UriKind.Absolute, out var directory) && directory.Scheme is "http" or "https"
            ? directory
            : null;
}
