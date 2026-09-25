namespace PrimeScore.Modules.Ingestion.Sources.Fred;

/// <summary>
/// Bound from <c>Fred</c>. The API key comes from user-secrets (Development) or the
/// <c>Fred__ApiKey</c> environment variable, never from a committed file (SRS SEC-002).
/// </summary>
internal sealed class FredOptions
{
    public const string Section = "Fred";

    public const string SourceName = "FRED";

    public const string HttpClientName = "fred";

    public bool Enabled { get; set; } = true;

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.stlouisfed.org/";

    /// <summary>UTC time of the daily pull. FRED publishes the previous session's closes by the next morning.</summary>
    public TimeOnly DailyRunUtc { get; set; } = new(13, 30);

    /// <summary>First observation date of a backfill; 2011 lets a 1,260-close window fill by 2016 (brief §7).</summary>
    public DateOnly BackfillStart { get; set; } = new(2011, 1, 1);

    /// <summary>Observations re-requested before the newest recorded one, to pick up late additions.</summary>
    public int OverlapDays { get; set; } = 10;

    public int RequestsPerMinute { get; set; } = 60;

    public int CacheHours { get; set; } = 6;

    /// <summary>Base of the quadratic retry back-off: waits of 1×, 4× and 9× this many seconds.</summary>
    public double RetryDelaySeconds { get; set; } = 5;

    /// <summary>Response cache; defaults to <c>cache/fred</c> next to the engine database.</summary>
    public string? CacheDirectory { get; set; }

    public bool RunOnStartup { get; set; }

    /// <summary>Cross-asset basket prices recorded as CROSS_ASSET_FLOW observations (brief §7).</summary>
    public List<string> BasketSeries { get; set; } = ["SP500", "DGS10", "DCOILWTICO", "DEXUSEU"];

    public string? DisabledReason() =>
        !Enabled ? "disabled by configuration (Fred:Enabled=false)"
        : string.IsNullOrWhiteSpace(ApiKey) ? "no FRED API key configured (set Fred:ApiKey with user-secrets or the Fred__ApiKey environment variable)"
        : null;
}
