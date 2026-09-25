using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Sources.Fred;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Ingestion.Sources;

/// <summary>On-demand pull requests and the scheduler's live state, shared with the status query.</summary>
internal sealed class SourcePullQueue
{
    private readonly Channel<string> _requests = Channel.CreateBounded<string>(new BoundedChannelOptions(4)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    public bool Running { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }

    public ChannelReader<string> Requests => _requests.Reader;

    public bool TryRequest(string source) => _requests.Writer.TryWrite(source);
}

/// <summary>Operational record of adapter runs (not ledger facts; the recorded signals are).</summary>
internal sealed class SourceRunStore(EngineDatabase database)
{
    public async Task<long> StartAsync(string source, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        await using var context = new IngestionDbContext(database.ContextOptions<IngestionDbContext>(IngestionDbContext.HistoryTable));
        var run = new SourceRunRow { Source = source, StartedAtMs = startedAt.ToUnixTimeMilliseconds() };
        context.SourceRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return run.Id;
    }

    /// <param name="partial">The run recorded what it could; <paramref name="error"/> lists what it could not.</param>
    public async Task FinishAsync(long id, DateTimeOffset finishedAt, SourceRunCounts? counts, string? error, CancellationToken cancellationToken, bool partial = false)
    {
        await using var context = new IngestionDbContext(database.ContextOptions<IngestionDbContext>(IngestionDbContext.HistoryTable));
        var run = await context.SourceRuns.SingleAsync(row => row.Id == id, cancellationToken).ConfigureAwait(false);
        run.FinishedAtMs = finishedAt.ToUnixTimeMilliseconds();
        run.Succeeded = error is null || partial;
        run.Error = error;
        if (counts is not null)
        {
            (run.Accepted, run.Duplicates, run.Revised, run.Missing, run.Rejected) =
                (counts.Accepted, counts.Duplicates, counts.Revised, counts.Missing, counts.Rejected);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Runs the FRED pull daily at <c>Fred:DailyRunUtc</c> and whenever the UI requests it. A
/// failed run is recorded with its (redacted) error; nothing a run does can stop the engine.
/// </summary>
internal sealed partial class SourceScheduler(
    IServiceScopeFactory scopes,
    SourcePullQueue queue,
    SourceRunStore runs,
    IOptions<FredOptions> options,
    IClock clock,
    ILogger<SourceScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.DisabledReason() is { } reason)
        {
            LogDisabled(reason);
        }
        else if (settings.RunOnStartup)
        {
            await RunAsync(stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.UtcNow;
            var next = NextRun(now, settings.DailyRunUtc);
            queue.NextRunAt = settings.DisabledReason() is null ? next : null;
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var delay = Task.Delay(Max(next - now, TimeSpan.Zero), wake.Token);
            var request = queue.Requests.WaitToReadAsync(wake.Token).AsTask();
            try
            {
                await Task.WhenAny(delay, request).ConfigureAwait(false);
            }
            finally
            {
                await wake.CancelAsync().ConfigureAwait(false);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            while (queue.Requests.TryRead(out _))
            {
                // Coalesce queued requests into the run below.
            }

            if (settings.DisabledReason() is null)
            {
                await RunAsync(stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal static DateTimeOffset NextRun(DateTimeOffset now, TimeOnly dailyUtc)
    {
        var today = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(dailyUtc), TimeSpan.Zero);
        return today > now ? today : today.AddDays(1);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        queue.Running = true;
        long? runId = null;
        try
        {
            runId = await runs.StartAsync(FredOptions.SourceName, clock.UtcNow, stoppingToken).ConfigureAwait(false);
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<FredPuller>().PullAsync(stoppingToken).ConfigureAwait(false);
            var detail = result.SeriesErrors.Count == 0 ? null : $"{result.SeriesErrors.Count} series not pulled: {string.Join(" | ", result.SeriesErrors)}";

            // Partial when some series were pulled; failed when none was.
            await runs.FinishAsync(runId.Value, clock.UtcNow, result.Counts, detail, stoppingToken, partial: detail is not null && !result.NothingPulled)
                .ConfigureAwait(false);
            if (detail is not null)
            {
                LogPartial(detail);
            }

            LogCompleted(result.Counts.Accepted, result.Counts.Duplicates, result.Counts.Revised, result.Counts.Missing);
            if (result.Recorded.Count > 0)
            {
                await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                    .PublishAsync(new SignalBatchAccepted(CorrelationId.New(), result.Recorded), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            var key = options.Value.ApiKey;
            var message = string.IsNullOrEmpty(key) ? exception.Message : exception.Message.Replace(key, "***", StringComparison.Ordinal);
            LogFailed(message);
            if (runId is { } id)
            {
                try
                {
                    await runs.FinishAsync(id, clock.UtcNow, null, message, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception recordFailure)
                {
                    // The database itself may be the fault (full disk); the engine keeps running.
                    LogRecordFailed(recordFailure.Message);
                }
            }
        }
        finally
        {
            queue.Running = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "FRED adapter disabled: {Reason}")]
    private partial void LogDisabled(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "FRED pull recorded {Accepted} signals ({Duplicates} duplicates, {Revised} revised values kept as first recorded, {Missing} missing values skipped)")]
    private partial void LogCompleted(int accepted, int duplicates, int revised, int missing);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FRED pull partial: {Detail}")]
    private partial void LogPartial(string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "FRED pull failed: {Error}")]
    private partial void LogFailed(string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "FRED run record could not be written: {Error}")]
    private partial void LogRecordFailed(string error);
}
