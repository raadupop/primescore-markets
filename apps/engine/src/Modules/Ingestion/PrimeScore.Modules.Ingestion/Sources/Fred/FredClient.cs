using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Ingestion.Sources.Fred;

/// <param name="Value">Null when FRED reports the observation as missing ("."); it is never filled in.</param>
internal sealed record FredObservation(DateOnly Date, double? Value);

/// <summary>
/// FRED <c>series/observations</c> over HTTPS for the cross-check. Nothing is written to disk: FRED
/// terms forbid storing FRED content in a database, so responses live in memory for one run only
/// (ADR-0009). Requests are rate-limited and retried; the API key and FRED's response bodies never
/// appear in logs or errors (only FRED's own error message, with the key removed).
/// </summary>
internal sealed partial class FredClient(
    IHttpClientFactory httpClientFactory,
    IOptions<FredOptions> options,
    IClock clock,
    ILogger<FredClient> logger)
{
    private static readonly SemaphoreSlim Throttle = new(1, 1);
    private static DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    /// <summary>Observations dated <paramref name="start"/> to <paramref name="end"/>, both inclusive, as FRED currently reports them.</summary>
    public async Task<IReadOnlyList<FredObservation>> GetObservationsAsync(
        string seriesId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var query = new StringBuilder()
            .Append("fred/series/observations?file_type=json&series_id=").Append(Uri.EscapeDataString(seriesId))
            .Append("&observation_start=").Append(start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append("&observation_end=").Append(end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append("&api_key=").Append(Uri.EscapeDataString(settings.ApiKey ?? ""));
        var body = await FetchAsync(query.ToString(), seriesId, settings, cancellationToken).ConfigureAwait(false);
        return Parse(body, seriesId);
    }

    /// <summary>Removes any occurrence of the API key from text that may be logged or stored.</summary>
    public string Redact(string text)
    {
        var key = options.Value.ApiKey;
        return string.IsNullOrEmpty(key) ? text : text.Replace(key, "***", StringComparison.Ordinal);
    }

    /// <summary>Parses a success body. A malformed body is reported without echoing any of it.</summary>
    internal static IReadOnlyList<FredObservation> Parse(string body, string seriesId)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("observations", out var observations) || observations.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("no observations array");
            }

            var result = new List<FredObservation>(observations.GetArrayLength());
            foreach (var observation in observations.EnumerateArray())
            {
                var date = DateOnly.ParseExact(observation.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var text = observation.GetProperty("value").GetString();
                double? value = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                    ? number
                    : null;
                result.Add(new FredObservation(date, value));
            }

            return result;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException($"FRED {seriesId}: malformed response", exception);
        }
    }

    private async Task<string> FetchAsync(string relativeUrl, string seriesId, FredOptions settings, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(FredOptions.HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            await WaitForSlotAsync(settings, cancellationToken).ConfigureAwait(false);
            try
            {
                using var response = await client.GetAsync(new Uri(relativeUrl, UriKind.Relative), cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                if (attempt < 4 && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
                {
                    LogRetry(seriesId, (int)response.StatusCode, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds * attempt * attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var message = ErrorMessage(body) is { } text ? " " + text : "";
                throw new InvalidOperationException(Redact(string.Create(
                    CultureInfo.InvariantCulture, $"FRED {seriesId}: HTTP {(int)response.StatusCode}{message}")));
            }
            catch (Exception exception) when (attempt < 4
                && (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                // TaskCanceledException without a cancellation request is the HttpClient timeout.
                LogRetry(seriesId, 0, attempt);
                await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds * attempt * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// FRED's <c>error_message</c> from a JSON error body (e.g. "The series does not exist."), so an
    /// unknown mapping is named; anything else in an error body is dropped.
    /// </summary>
    private static string? ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error_message", out var error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() is { Length: > 0 } text
                    ? text.Length <= 200 ? text : text[..200]
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WaitForSlotAsync(FredOptions settings, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(60.0 / Math.Max(1, settings.RequestsPerMinute));
        await Throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Never longer than one interval, so a clock set back (or a different test clock) cannot stall requests.
            var wait = _nextRequestAt - clock.UtcNow;
            wait = wait > interval ? interval : wait;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _nextRequestAt = clock.UtcNow + interval;
        }
        finally
        {
            Throttle.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FRED {SeriesId}: attempt {Attempt} failed (HTTP {Status}); retrying")]
    private partial void LogRetry(string seriesId, int status, int attempt);
}
