using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Ingestion.Sources.Cboe;

/// <summary>
/// Polls Cboe's daily index history files (v3 design §4, §5.1) from 18:00 New York until 08:00 the
/// next day and records each close not yet recorded, stamped 16:15 New York (SPX 16:00) on its data
/// date; the first run records the whole file. Conditional GETs (ETag, Last-Modified) and a body hash skip
/// unchanged files; the validators are kept only after a file's closes were recorded, so any
/// failure on the way makes the next poll download and try again in full. Cboe terms: internal
/// use, no redistribution of the series.
/// </summary>
internal sealed partial class CboeIndexAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<CboeOptions> options,
    SignalRecorder recorder,
    IngestionReadStore reads,
    IClock clock,
    ILogger<CboeIndexAdapter> logger) : ISourceAdapter
{
    /// <summary>Validators of the last fully recorded body per symbol; in memory, so a restart downloads each file once.</summary>
    private readonly ConcurrentDictionary<string, FileState> _files = new(StringComparer.Ordinal);

    public string Name => CboeOptions.SourceName;

    public string Description => "Cboe daily index history files (volatility indices and SPX): closes stamped 16:15 New York (SPX 16:00); internal use, no redistribution.";

    public string? DisabledReason => SourceOptions.TryRead(options, CboeOptions.Section, out var invalid) is { } settings ? settings.DisabledReason() : invalid;

    public bool RunOnStartup => SourceOptions.TryRead(options, CboeOptions.Section, out _)?.RunOnStartup ?? false;

    /// <summary>Every day, Friday's VIX close can land on Sunday.</summary>
    public SourceSchedule Schedule => SourceOptions.TryRead(options, CboeOptions.Section, out _) is { } settings
        ? new([], [new PollingWindow(settings.WindowStartNewYork, settings.WindowEndNewYork, settings.PollInterval, SourceDays.EveryDay)])
        : SourceSchedule.OnRequest;

    public IReadOnlyList<SourceSeries> Series => SourceOptions.TryRead(options, CboeOptions.Section, out _) is { } settings
        ? settings.SymbolList
            .Select(CboeIndexCatalog.Find)
            .OfType<CboeIndex>()
            .Select(index => new SourceSeries(
                index.Symbol, index.SourceIdentifier, index.Symbol, index.Category, MappingVerified: true, index.Timing, settings.TryFileUrl(index.Symbol)))
            .ToArray()
        : [];

    public async Task<SourcePullResult> PullAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var symbols = settings.SymbolList;
        var tally = new Tally();
        var errors = new List<string>();
        foreach (var symbol in symbols)
        {
            try
            {
                await PullFileAsync(CboeIndexCatalog.Find(symbol) ?? throw new InvalidOperationException("unknown symbol"), settings, tally, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One unavailable file (404, a challenge page, a changed layout, a busy database) must not block the others.
                errors.Add($"{symbol}: {exception.Message}");
                LogFileFailed(symbol, exception.Message);
            }
        }

        var note = string.Create(
            CultureInfo.InvariantCulture,
            $"{tally.Changed} of {symbols.Count} files changed; {tally.Accepted} new closes recorded")
            + (tally.NotYetDue > 0
                ? string.Create(CultureInfo.InvariantCulture, $"; {tally.NotYetDue} closes dated after the fetch held for a later poll")
                : "")
            + ".";
        return new SourcePullResult(
            new SourceRunCounts(tally.Accepted, tally.Duplicates, tally.Revised, tally.Missing, Rejected: 0),
            tally.Recorded, errors, symbols.Count, [], note);
    }

    private async Task PullFileAsync(CboeIndex index, CboeOptions settings, Tally tally, CancellationToken cancellationToken)
    {
        var url = settings.FileUrl(index.Symbol);
        _files.TryGetValue(index.Symbol, out var previous);
        var fetched = await FetchAsync(index.Symbol, url, previous, settings, cancellationToken).ConfigureAwait(false);
        if (fetched is null)
        {
            return;
        }

        var (body, etag, lastModified) = fetched.Value;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(body));
        if (previous is not null && previous.BodySha256 == sha256)
        {
            return;
        }

        var retrievedAt = clock.UtcNow;
        tally.Changed++;
        var file = CboeCsv.Parse(index.Symbol, body);
        var recorded = await RecordedDatesAsync(index, cancellationToken).ConfigureAwait(false);

        // A close is due once its stamp has passed; an observation is never stamped after its fetch.
        var due = file.Closes.Where(close => index.ObservedAt(close.Date) <= retrievedAt).ToArray();
        var notYetDue = file.Closes.Count - due.Length;
        tally.NotYetDue += notYetDue;
        var fresh = due.Where(close => !recorded.Contains(close.Date)).ToArray();
        if (fresh.Length > 0)
        {
            // Recent recorded closes ride along so a revised value is counted; ascending, so an interrupted backfill resumes.
            var overlap = due.Where(close => recorded.Contains(close.Date)).TakeLast(settings.OverlapRows);
            var candidates = fresh.Concat(overlap).OrderBy(close => close.Date)
                .Select(close => (object)Candidate(index, close, url, retrievedAt, sha256))
                .ToArray();
            var outcomes = await recorder.RecordAsync(candidates, cancellationToken).ConfigureAwait(false);
            tally.Add(outcomes);

            // A gap is counted once, by the run that records the first close after it: a missing date
            // after the newest recorded one and before the newest fresh one (all on the first run). A
            // missing last row waits for a later close, so the next poll does not count it again.
            var newestRecorded = recorded.Count == 0 ? DateOnly.MinValue : recorded.Max();
            var newestFresh = fresh.Max(close => close.Date);
            tally.Missing += file.Missing.Count(date => date > newestRecorded && date < newestFresh);
        }

        if (notYetDue == 0)
        {
            _files[index.Symbol] = new FileState(etag, lastModified, sha256);
        }
    }

    /// <summary>
    /// The signal for one close. MARKET_DATA rows carry the payload an API caller would submit
    /// (tenor in the variant for VIX9D, VIX3M, VIX6M); SPX follows the cross-asset basket convention.
    /// </summary>
    internal static SignalCandidate Candidate(CboeIndex index, CboeClose close, Uri url, DateTimeOffset retrievedAt, string fileSha256)
    {
        var observedAt = index.ObservedAt(close.Date);
        var reconstructed = index.Reconstructed(close.Date);
        var note = index.Timing + (reconstructed switch
        {
            true => $"; back-calculated by Cboe before the index went live ({index.LiveStart})",
            null => "; live start unverified",
            false => "",
        });
        var provenance = new SignalProvenance(
            CboeOptions.SourceName,
            SeriesId: index.Symbol,
            Url: url.ToString(),
            RetrievedAt: retrievedAt,
            MappingVerified: true,
            Note: note,
            FileSha256: fileSha256,
            Reconstructed: reconstructed);
        return new SignalCandidate(
            index.Category, index.SourceIdentifier, index.Symbol, index.Variant, observedAt, "STRUCTURED", close.Close,
            Payload(index, close, observedAt), provenance);
    }

    private static JsonElement Payload(CboeIndex index, CboeClose close, DateTimeOffset observedAt)
    {
        Dictionary<string, object?> payload = index.Category == SourceCategory.MarketData
            ? new()
            {
                ["asset_class"] = index.AssetClass,
                ["instrument"] = index.Symbol,
                ["metric_type"] = CboeIndexCatalog.ImpliedVolatility,
                ["tenor"] = index.Tenor,
                ["value"] = close.Close,
                ["unit"] = index.Unit,
                ["observed_at"] = observedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            }
            : new()
            {
                ["kind"] = "basket_observation",
                ["instrument"] = index.Symbol,
                ["value"] = close.Close,
                ["unit"] = index.Unit,
                ["observation_date"] = close.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
        return JsonSerializer.SerializeToElement(payload.Where(pair => pair.Value is not null).ToDictionary());
    }

    /// <summary>New York dates this adapter already recorded for the index (rows of other providers do not count).</summary>
    private async Task<HashSet<DateOnly>> RecordedDatesAsync(CboeIndex index, CancellationToken cancellationToken)
    {
        await using var context = reads.Open();
        var stamps = await context.Signals
            .Where(row => row.SourceIdentifier == index.SourceIdentifier && row.Provider == CboeOptions.SourceName)
            .Select(row => row.ObservedAtMs)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return stamps.Select(ms => MarketTime.NewYorkDate(DateTimeOffset.FromUnixTimeMilliseconds(ms))).ToHashSet();
    }

    /// <summary>The file body, or null when the server answers 304 Not Modified. Errors name the status, never the body.</summary>
    private async Task<(byte[] Body, string? ETag, DateTimeOffset? LastModified)?> FetchAsync(
        string symbol, Uri url, FileState? previous, CboeOptions settings, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(CboeOptions.HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (previous?.ETag is { } etag)
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                }

                if (previous?.LastModified is { } modified)
                {
                    request.Headers.IfModifiedSince = modified;
                }

                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return null;
                }

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    return (body, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified);
                }

                // Cboe's CDN refuses some back-to-back requests with 403 and serves the same file seconds later
                // (1 to 3 of 10 files on every live run of 2026-10-01), so 403 is retried like a rate limit.
                if (attempt < 4 && (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden || (int)response.StatusCode >= 500))
                {
                    LogRetry(symbol, (int)response.StatusCode, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds * attempt * attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode}"));
            }
            catch (Exception exception) when (attempt < 4
                && (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                // TaskCanceledException without a cancellation request is the HttpClient timeout.
                LogRetry(symbol, 0, attempt);
                await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds * attempt * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cboe {Symbol}: attempt {Attempt} failed (HTTP {Status}); retrying")]
    private partial void LogRetry(string symbol, int status, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cboe {Symbol}: file not pulled ({Reason})")]
    private partial void LogFileFailed(string symbol, string reason);

    private sealed record FileState(string? ETag, DateTimeOffset? LastModified, string BodySha256);

    private sealed class Tally
    {
        public int Accepted { get; private set; }

        public int Duplicates { get; private set; }

        public int Revised { get; private set; }

        public int Missing { get; set; }

        public int Changed { get; set; }

        public int NotYetDue { get; set; }

        public List<Guid> Recorded { get; } = [];

        public void Add(IReadOnlyList<RecordOutcome> outcomes)
        {
            foreach (var outcome in outcomes)
            {
                switch (outcome.Status)
                {
                    case RecordStatus.Recorded:
                        Accepted++;
                        Recorded.Add(outcome.Id);
                        break;
                    case RecordStatus.Duplicate:
                        Duplicates++;
                        break;
                    case RecordStatus.Revised:
                        Revised++;
                        break;
                }
            }
        }
    }
}
