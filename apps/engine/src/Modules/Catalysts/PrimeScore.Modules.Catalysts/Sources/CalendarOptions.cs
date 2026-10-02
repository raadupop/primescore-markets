using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PrimeScore.Modules.Catalysts.Sources;

/// <summary>
/// Settings every calendar adapter shares, bound from its own <c>Sources:&lt;Name&gt;</c> section
/// (ADR-0011); disabled unless <c>Enabled</c> is true. Each request sends <see cref="UserAgent"/>,
/// which should name a contact: originators may block robots that do not.
/// </summary>
internal abstract partial class CalendarOptions(string section, string baseUrl)
{
    public bool Enabled { get; set; }

    /// <summary>Root the adapter's page paths are resolved against.</summary>
    public string BaseUrl { get; set; } = baseUrl;

    public string UserAgent { get; set; } = $"PrimeScoreMarkets/0.1 (+calendar reader; set {section}:UserAgent with a contact)";

    /// <summary>One run each weekday at this New York time.</summary>
    public TimeOnly DailyRunNewYork { get; set; } = new(6, 0);

    public bool RunOnStartup { get; set; }

    /// <summary>Base of the quadratic retry back-off: waits of 1× and 4× this many seconds.</summary>
    public double RetryDelaySeconds { get; set; } = 5;

    /// <summary>Per request, including the body.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Null when the adapter may run; otherwise why not, naming the key but never its value.</summary>
    public virtual string? DisabledReason()
    {
        if (!Enabled)
        {
            return $"disabled by configuration (set {section}:Enabled=true)";
        }

        if (BaseDirectory() is null)
        {
            return $"{section}:BaseUrl must be an absolute http(s) URL";
        }

        if (string.IsNullOrWhiteSpace(UserAgent) || UserAgent.Any(char.IsControl))
        {
            return $"{section}:UserAgent must be one line of text";
        }

        return TimeoutSeconds <= 0 ? $"{section}:TimeoutSeconds must be positive"
            : RetryDelaySeconds < 0 ? $"{section}:RetryDelaySeconds must not be negative"
            : null;
    }

    /// <summary>The page at <paramref name="path"/> under <see cref="BaseUrl"/>; only called once <see cref="DisabledReason"/> has passed.</summary>
    public Uri PageUrl(string path) =>
        new(BaseDirectory() ?? throw new InvalidOperationException($"{section}:BaseUrl must be an absolute http(s) URL"), path);

    private Uri? BaseDirectory() =>
        Uri.TryCreate(BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/", UriKind.Absolute, out var directory) && directory.Scheme is "http" or "https"
            ? directory
            : null;

    /// <summary>
    /// Reads the bound options without letting a value the binder cannot convert (<c>Enabled=yes</c>)
    /// escape: every read of <see cref="IOptions{T}.Value"/> throws again then, and the status page,
    /// the pull command and the scheduler read these properties. The reason names the key, never the
    /// value, which the binder's own message quotes and which may be a misplaced secret. (A copy of
    /// Ingestion's internal <c>SourceOptions.TryRead</c>.)
    /// </summary>
    public static T? TryRead<T>(IOptions<T> options, string section, out string? invalid)
        where T : CalendarOptions
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            invalid = null;
            return options.Value;
        }
        catch (InvalidOperationException exception)
        {
            var key = BinderKey().Match(exception.Message) is { Success: true } match ? match.Groups["key"].Value : section;
            invalid = $"invalid configuration: {key} has a value that is not of its type";
            return null;
        }
    }

    [GeneratedRegex(" at '(?<key>[^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex BinderKey();
}
