using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Ledger;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Ingestion.Sources.Fred;

/// <param name="RealtimeStart">For initial-release requests, the date the value was first published.</param>
/// <param name="Value">Null when FRED reports the observation as missing ("."); it is never filled in.</param>
internal sealed record FredObservation(DateOnly Date, DateOnly RealtimeStart, double? Value);

/// <param name="RetrievedAt">When FRED served the body: now, or the cache file's write time.</param>
internal sealed record FredResponse(IReadOnlyList<FredObservation> Observations, DateTimeOffset RetrievedAt);

/// <summary>
/// FRED <c>series/observations</c> over HTTPS. Backfill responses are cached on disk (FRED terms
/// ask callers to cache; a rerun of a backfill does not refetch 15 years), incremental pulls
/// always ask FRED. Requests are rate-limited and retried, and the API key never appears in
/// logs, errors or the cache.
/// </summary>
internal sealed partial class FredClient(
    IHttpClientFactory httpClientFactory,
    IOptions<FredOptions> options,
    EngineDatabase database,
    IClock clock,
    ILogger<FredClient> logger)
{
    private static readonly SemaphoreSlim Throttle = new(1, 1);
    private static DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    /// <param name="allowCache">True only for backfills; an incremental pull must see the newest release.</param>
    public async Task<FredResponse> GetObservationsAsync(
        string seriesId,
        DateOnly observationStart,
        bool initialRelease,
        bool allowCache,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var query = new StringBuilder()
            .Append("fred/series/observations?file_type=json&series_id=").Append(Uri.EscapeDataString(seriesId))
            .Append("&observation_start=").Append(observationStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (initialRelease)
        {
            // output_type=4: each observation as first released, with its first release date.
            query.Append("&output_type=4&realtime_start=1776-07-04&realtime_end=9999-12-31");
        }

        var cacheFile = CachePath(settings, query.ToString());
        if (allowCache && ReadFreshCache(cacheFile, settings.CacheHours) is { } cached)
        {
            return new FredResponse(Parse(cached.Body, seriesId), cached.WrittenAt);
        }

        var body = await FetchAsync(
            query.Append("&api_key=").Append(Uri.EscapeDataString(settings.ApiKey ?? "")).ToString(),
            seriesId, settings, cancellationToken).ConfigureAwait(false);
        var response = new FredResponse(Parse(body, seriesId), clock.UtcNow);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, body, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimisation; a full disk or locked file must not fail the pull.
            LogCacheFailed(seriesId, exception.Message);
        }

        return response;
    }

    /// <summary>Removes any occurrence of the API key from text that may be logged or stored.</summary>
    public string Redact(string text)
    {
        var key = options.Value.ApiKey;
        return string.IsNullOrEmpty(key) ? text : text.Replace(key, "***", StringComparison.Ordinal);
    }

    internal static IReadOnlyList<FredObservation> Parse(string body, string seriesId)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("observations", out var observations) || observations.ValueKind != JsonValueKind.Array)
        {
            var message = document.RootElement.TryGetProperty("error_message", out var error) ? error.GetString() : "no observations array";
            throw new InvalidOperationException($"FRED {seriesId}: {message}");
        }

        var result = new List<FredObservation>(observations.GetArrayLength());
        foreach (var observation in observations.EnumerateArray())
        {
            var date = DateOnly.ParseExact(observation.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var realtime = DateOnly.ParseExact(observation.GetProperty("realtime_start").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var text = observation.GetProperty("value").GetString();
            double? value = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                ? number
                : null;
            result.Add(new FredObservation(date, realtime, value));
        }

        return result;
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

                throw new InvalidOperationException(Redact($"FRED {seriesId}: HTTP {(int)response.StatusCode} {Truncate(body)}"));
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

    private async Task WaitForSlotAsync(FredOptions settings, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(60.0 / Math.Max(1, settings.RequestsPerMinute));
        await Throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _nextRequestAt - clock.UtcNow;
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

    private string CachePath(FredOptions settings, string requestWithoutKey)
    {
        var directory = settings.CacheDirectory
            ?? Path.Combine(Path.GetDirectoryName(database.FullPath) ?? ".", "cache", "fred");
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requestWithoutKey)))[..32];
        return Path.Combine(directory, name + ".json");
    }

    private (string Body, DateTimeOffset WrittenAt)? ReadFreshCache(string path, int cacheHours)
    {
        try
        {
            if (cacheHours <= 0 || !File.Exists(path))
            {
                return null;
            }

            var writtenAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return clock.UtcNow - writtenAt > TimeSpan.FromHours(cacheHours) ? null : (File.ReadAllText(path), writtenAt);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Truncate(string body) => body.Length <= 300 ? body : body[..300];

    [LoggerMessage(Level = LogLevel.Warning, Message = "FRED {SeriesId}: attempt {Attempt} failed (HTTP {Status}); retrying")]
    private partial void LogRetry(string seriesId, int status, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FRED {SeriesId}: response not cached ({Reason})")]
    private partial void LogCacheFailed(string seriesId, string reason);
}
