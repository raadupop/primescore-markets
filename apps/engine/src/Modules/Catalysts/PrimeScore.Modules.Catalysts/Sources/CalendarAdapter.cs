using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources;

/// <summary>
/// Shared run of the calendar adapters (ADR-0011): fetch each page with the configured user
/// agent, retry 429, 5xx and timeouts, hash the bytes, parse, then record every page read in this
/// run through one <see cref="CatalystRecorder"/> call (listings before archives, as the adapter
/// adds them). Adapters that read no web page (a rule, a local file) add their snapshot to the run
/// themselves. A page that fails, or whose layout is not recognised, is an item error and
/// contributes nothing; rejected rows are item errors too, so the scheduler marks the run partial.
/// Errors name the HTTP status, never the body. The counts put every new ledger entry (new
/// catalysts and new vintages) in Accepted and the split in the note: Revised would read
/// "first recorded values retained" on the Sources page, which is not what a calendar does.
/// </summary>
internal abstract partial class CalendarAdapter(
    IHttpClientFactory httpClientFactory,
    CatalystRecorder recorder,
    CatalystsReadStore reads,
    IClock clock,
    ILogger logger) : ISourceAdapter
{
    public const string HttpClientName = "catalysts";

    private const int Attempts = 3;

    public abstract string Name { get; }

    public abstract string Description { get; }

    public string? DisabledReason => ReadSettings(out var invalid) is { } settings ? settings.DisabledReason() : invalid;

    public bool RunOnStartup => ReadSettings(out _)?.RunOnStartup ?? false;

    /// <summary>One run each weekday; release calendars change rarely and never at a particular hour.</summary>
    public SourceSchedule Schedule => ReadSettings(out _) is { } settings
        ? new([new DailyRun(settings.DailyRunNewYork, SourceDays.Weekdays)], [])
        : SourceSchedule.OnRequest;

    /// <summary>Calendars record catalysts, not signal series.</summary>
    public IReadOnlyList<SourceSeries> Series => [];

    public async Task<SourcePullResult> PullAsync(CancellationToken cancellationToken)
    {
        var run = new CalendarRun();
        await ReadPagesAsync(run, cancellationToken).ConfigureAwait(false);
        var tally = run.Snapshots.Count == 0
            ? new RecordTally(0, 0, 0, [])
            : await recorder.RecordAsync(run.Snapshots, force: false, cancellationToken).ConfigureAwait(false);

        var skipped = run.Skipped.Count == 0
            ? ""
            : "; skipped: " + string.Join(", ", run.Skipped.Select(pair =>
                string.Create(CultureInfo.InvariantCulture, $"{pair.Value} {pair.Key}{(pair.Value == 1 ? "" : "s")}")));
        var note = string.Create(
            CultureInfo.InvariantCulture,
            $"{ReadSummary(run)}; {tally.Scheduled} new, {tally.Rescheduled} rescheduled, {tally.Unchanged} unchanged{skipped}.");
        return new SourcePullResult(
            new SourceRunCounts(tally.Scheduled + tally.Rescheduled, tally.Unchanged, Revised: 0, Missing: 0, run.RejectedRows),
            [], run.Errors, run.PagesAttempted + run.RowsExamined, tally.Flags, note);
    }

    /// <summary>The recording clock: fetch times and rule windows.</summary>
    protected IClock Clock => clock;

    /// <summary>The note's opening: how much of the source was read.</summary>
    protected virtual string ReadSummary(CalendarRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return string.Create(CultureInfo.InvariantCulture, $"{run.PagesRead} of {run.PagesAttempted} pages read");
    }

    /// <summary>The adapter's bound options, or null with the reason when a value does not bind.</summary>
    protected abstract CalendarOptions? ReadSettings(out string? invalid);

    /// <summary>Reads the adapter's pages through <see cref="ReadPageAsync"/> (or its rule or file) and adds the snapshots in recording order.</summary>
    protected abstract Task ReadPagesAsync(CalendarRun run, CancellationToken cancellationToken);

    /// <summary>
    /// History rule: a historical page is fetched only while no recorded vintage carries its URL, so a
    /// page that failed is tried again on the next run and a page read once is not read again.
    /// </summary>
    protected Task<bool> AlreadyRecordedAsync(Uri url, CancellationToken cancellationToken) =>
        reads.HasRecordedFromUrlAsync(url.ToString(), cancellationToken);

    /// <summary>Fetches and parses one page; null when it failed, which the run holds as an item error.</summary>
    protected async Task<CalendarRead?> ReadPageAsync(
        CalendarRun run, CalendarOptions settings, string path, Func<string, ParsedCalendar> parse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parse);
        run.PagesAttempted++;
        try
        {
            var url = settings.PageUrl(path);
            var body = await FetchAsync(path, url, settings, cancellationToken).ConfigureAwait(false);
            var retrievedAt = Clock.UtcNow;
            var page = parse(Encoding.UTF8.GetString(body));
            run.PagesRead++;
            run.RowsExamined += page.RowsExamined;
            run.RejectedRows += page.Rejected.Count;
            run.Errors.AddRange(page.Rejected.Select(row => $"{path}: row not recognised: '{row}'"));
            foreach (var label in page.Skipped)
            {
                run.Skipped[label] = run.Skipped.GetValueOrDefault(label) + 1;
            }

            return new CalendarRead(url.ToString(), retrievedAt, Convert.ToHexStringLower(SHA256.HashData(body)), page);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // One unavailable or changed page must not block the others.
            run.Errors.Add($"{path}: {exception.Message}");
            LogPageFailed(Name, path, exception.Message);
            return null;
        }
    }

    /// <summary>A snapshot of one page read, carrying its URL, fetch time and SHA-256.</summary>
    protected static CalendarSnapshot Snapshot(
        string adapter, CatalystSourceKind kind, CalendarRead read, IReadOnlyCollection<CatalystFamily> families, int proximityDays)
    {
        ArgumentNullException.ThrowIfNull(read);
        return new CalendarSnapshot(
            adapter, kind, read.Url, read.RetrievedAt, read.FileSha256, families, read.Page.CoverageFrom, read.Page.CoverageTo,
            proximityDays, read.Page.Rows);
    }

    /// <summary>The body; 429, 5xx and timeouts are tried three times in all. Errors name the status, never the body.</summary>
    private async Task<byte[]> FetchAsync(string path, Uri url, CalendarOptions settings, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            string failure;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.TryAddWithoutValidation("User-Agent", settings.UserAgent);
                    using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                    }

                    var status = (int)response.StatusCode;
                    failure = string.Create(CultureInfo.InvariantCulture, $"HTTP {status}");
                    if (response.StatusCode != HttpStatusCode.TooManyRequests && status < 500)
                    {
                        throw new InvalidOperationException(failure);
                    }
                }
                catch (HttpRequestException)
                {
                    failure = "request failed";
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = "timed out";
                }
            }

            if (attempt >= Attempts)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{failure} after {Attempts} attempts"));
            }

            LogRetry(Name, path, failure, attempt);
            await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds * attempt * attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Adapter} {Path}: attempt {Attempt} failed ({Failure}); retrying")]
    private partial void LogRetry(string adapter, string path, string failure, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Adapter} {Path}: page not read ({Reason})")]
    protected partial void LogPageFailed(string adapter, string path, string reason);

    /// <summary>One page fetched and parsed.</summary>
    /// <param name="Url">The page URL as recorded; the history rule looks it up.</param>
    protected sealed record CalendarRead(string Url, DateTimeOffset RetrievedAt, string FileSha256, ParsedCalendar Page);

    /// <summary>What one pull has read so far.</summary>
    protected sealed class CalendarRun
    {
        public int PagesAttempted { get; set; }

        public int PagesRead { get; set; }

        public int RowsExamined { get; set; }

        public int RejectedRows { get; set; }

        public List<string> Errors { get; } = [];

        /// <summary>Skipped rows per label, in first-seen order.</summary>
        public Dictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);

        /// <summary>In recording order: listings before archives.</summary>
        public List<CalendarSnapshot> Snapshots { get; } = [];
    }
}
