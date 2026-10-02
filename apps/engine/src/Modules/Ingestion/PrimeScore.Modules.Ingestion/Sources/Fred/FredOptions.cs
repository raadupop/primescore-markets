namespace PrimeScore.Modules.Ingestion.Sources.Fred;

/// <summary>
/// Bound from <c>Sources:Fred</c>; disabled unless <c>Enabled</c> is true and a key is set. FRED is a
/// read-only cross-check of Cboe closes and records nothing (ADR-0009). The API key comes from
/// user-secrets (Development) or the <c>Sources__Fred__ApiKey</c> environment variable, never from a
/// committed file (SRS SEC-002). Invalid values keep the adapter disabled with a reason naming the key.
/// </summary>
internal sealed class FredOptions
{
    public const string Section = "Sources:Fred";

    /// <summary>Where the key lived while FRED recorded signals; still detected so the operator is told to move it.</summary>
    public const string LegacyApiKey = "Fred:ApiKey";

    public const string SourceName = "FRED";

    public const string HttpClientName = "fred";

    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.stlouisfed.org/";

    /// <summary>New York time of the daily cross-check; FRED publishes the previous session's closes by the next morning.</summary>
    public TimeOnly DailyRunNewYork { get; set; } = new(8, 15);

    public bool RunOnStartup { get; set; }

    public int RequestsPerMinute { get; set; } = 60;

    /// <summary>Base of the quadratic retry back-off: waits of 1×, 4× and 9× this many seconds.</summary>
    public double RetryDelaySeconds { get; set; } = 5;

    /// <summary>The most recent recorded Cboe dates compared per series.</summary>
    public int CompareDays { get; set; } = 5;

    /// <summary>A difference is flagged when it exceeds the larger of this and <see cref="RelativeTolerance"/> × |Cboe close|.</summary>
    public double AbsoluteTolerance { get; set; } = 0.005;

    public double RelativeTolerance { get; set; } = 0.0001;

    /// <summary>Cboe symbols not cross-checked (e.g. a mapping FRED does not know). Starts empty, so configured items are the whole list.</summary>
    public string[] ExcludedSymbols { get; set; } = [];

    /// <summary>Set at registration when <see cref="LegacyApiKey"/> holds a value; not bound from configuration.</summary>
    internal bool LegacyKeyConfigured { get; set; }

    public string? DisabledReason()
    {
        var legacy = LegacyKeyConfigured && string.IsNullOrWhiteSpace(ApiKey)
            ? $"; the key under {LegacyApiKey} is no longer read, move it to Sources:Fred:ApiKey"
            : "";
        if (!Enabled)
        {
            return "disabled by configuration (set Sources:Fred:Enabled=true)" + legacy;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return "no FRED API key configured (set Sources:Fred:ApiKey with user-secrets or the Sources__Fred__ApiKey environment variable)" + legacy;
        }

        return !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("http" or "https")
                ? "Sources:Fred:BaseUrl must be an absolute http(s) URL"
            : CompareDays < 1 ? "Sources:Fred:CompareDays must be at least 1"
            : !(AbsoluteTolerance >= 0) ? "Sources:Fred:AbsoluteTolerance must not be negative"
            : !(RelativeTolerance >= 0) ? "Sources:Fred:RelativeTolerance must not be negative"
            : RequestsPerMinute < 1 ? "Sources:Fred:RequestsPerMinute must be at least 1"
            : RetryDelaySeconds < 0 ? "Sources:Fred:RetryDelaySeconds must not be negative"
            : null;
    }

    /// <summary>The flag threshold for a Cboe close: max(absolute, relative × |close|).</summary>
    public double Tolerance(double cboeClose) => Math.Max(AbsoluteTolerance, RelativeTolerance * Math.Abs(cboeClose));
}
