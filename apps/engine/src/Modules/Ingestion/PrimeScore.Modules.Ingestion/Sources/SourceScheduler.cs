using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Json;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Ingestion.Sources;

/// <summary>Live state of one adapter's loop and its on-demand pull requests.</summary>
internal sealed class SourceState
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(4)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private volatile bool _running;

    public bool Running
    {
        get => _running;
        set => _running = value;
    }

    public DateTimeOffset? NextRunAt { get; set; }

    public ChannelReader<bool> Requests => _requests.Reader;

    public bool TryRequest() => _requests.Writer.TryWrite(true);
}

/// <summary>Per-adapter live state keyed by adapter name (any letter case), shared by the scheduler, pull command and status query.</summary>
internal sealed class SourceRuntime
{
    private readonly ConcurrentDictionary<string, SourceState> _states = new(StringComparer.OrdinalIgnoreCase);

    public SourceState For(string source) => _states.GetOrAdd(source, _ => new SourceState());
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
    /// <param name="flags">Operational findings (a cross-check disagreement); kept on the run row, never in the ledger.</param>
    public async Task FinishAsync(
        long id,
        DateTimeOffset finishedAt,
        SourceRunCounts? counts,
        string? error,
        CancellationToken cancellationToken,
        bool partial = false,
        IReadOnlyList<string>? flags = null,
        string? note = null)
    {
        await using var context = new IngestionDbContext(database.ContextOptions<IngestionDbContext>(IngestionDbContext.HistoryTable));
        var run = await context.SourceRuns.SingleAsync(row => row.Id == id, cancellationToken).ConfigureAwait(false);
        run.FinishedAtMs = finishedAt.ToUnixTimeMilliseconds();
        run.Succeeded = error is null || partial;
        run.Error = error;
        run.Flags = flags is { Count: > 0 } ? CanonicalJson.Serialize(flags) : null;
        run.Note = note;
        if (counts is not null)
        {
            (run.Accepted, run.Duplicates, run.Revised, run.Missing, run.Rejected) =
                (counts.Accepted, counts.Duplicates, counts.Revised, counts.Missing, counts.Rejected);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Runs every registered <see cref="ISourceAdapter"/> on its own loop, concurrently: an optional
/// startup pull (which first waits for the startup pulls of adapters registered before it, so a
/// cross-check sees what an earlier adapter just recorded), then each <see cref="SourceSchedule"/>
/// slot and each on-demand request. Every iteration is isolated: a throwing adapter, schedule or
/// option is logged (redacted), recorded as a failed run, and retried after a back-off doubling
/// from <see cref="InitialBackoff"/> to one hour; nothing a run does can stop the engine.
/// Recorded ids are announced as <see cref="SignalBatchAccepted"/> by one coalescing publisher
/// loop, so classifying a large backfill never blocks another adapter or a pull request; ids still
/// queued at shutdown are picked up by the classification scheduler's pending run.
/// </summary>
internal sealed partial class SourceScheduler(
    IEnumerable<ISourceAdapter> adapters,
    SourceRuntime runtime,
    SourceRunStore runs,
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<SourceScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    /// <summary>How often a disabled adapter, or one without scheduled runs, re-reads its state without a request.</summary>
    private static readonly TimeSpan Recheck = TimeSpan.FromHours(1);

    private readonly ISourceAdapter[] _adapters = [.. adapters];
    private readonly Channel<IReadOnlyList<Guid>> _recorded = Channel.CreateUnbounded<IReadOnlyList<Guid>>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>First wait after a failed iteration; settable for tests.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startup = _adapters.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        return Task.WhenAll([PublishLoopAsync(stoppingToken), .. _adapters.Select((adapter, index) => LoopAsync(adapter, startup, index, stoppingToken))]);
    }

    private async Task LoopAsync(ISourceAdapter adapter, TaskCompletionSource[] startup, int index, CancellationToken stoppingToken)
    {
        await Task.Yield();
        var state = runtime.For(adapter.Name);
        var backoff = TimeSpan.Zero;
        var first = true;
        DateTimeOffset? lastSlot = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (first)
                {
                    first = false;
                    try
                    {
                        await StartupAsync(adapter, state, startup, index, stoppingToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        startup[index].TrySetResult();
                    }
                }

                var now = clock.UtcNow;
                var next = adapter.DisabledReason is null ? adapter.Schedule.NextAfter(Later(now, lastSlot)) : null;
                state.NextRunAt = next;
                var requested = await WaitAsync(state, next is { } at ? Positive(at - now) : Recheck, stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                while (state.Requests.TryRead(out _))
                {
                    // Coalesce queued requests into the run below.
                }

                lastSlot = requested ? lastSlot : next;
                if ((requested || next is not null) && adapter.DisabledReason is null)
                {
                    await RunAsync(adapter, state, stoppingToken).ConfigureAwait(false);
                }

                backoff = TimeSpan.Zero;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var message = Redact(adapter, exception.Message);
                LogLoopFailed(adapter.Name, message);
                await RecordFailureAsync(adapter.Name, message).ConfigureAwait(false);
                backoff = backoff == TimeSpan.Zero ? InitialBackoff : Min(backoff * 2, MaxBackoff);
                state.NextRunAt = null;
                try
                {
                    await WaitAsync(state, backoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task StartupAsync(ISourceAdapter adapter, SourceState state, TaskCompletionSource[] startup, int index, CancellationToken stoppingToken)
    {
        if (adapter.DisabledReason is { } reason)
        {
            LogDisabled(adapter.Name, reason);
            return;
        }

        if (!adapter.RunOnStartup)
        {
            return;
        }

        await Task.WhenAll(startup.Take(index).Select(earlier => earlier.Task)).WaitAsync(stoppingToken).ConfigureAwait(false);
        await RunAsync(adapter, state, stoppingToken).ConfigureAwait(false);
    }

    /// <returns>True when a pull request woke the loop; false when the delay elapsed.</returns>
    private static async Task<bool> WaitAsync(SourceState state, TimeSpan delay, CancellationToken stoppingToken)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var elapsed = Task.Delay(delay, wake.Token);
        var request = state.Requests.WaitToReadAsync(wake.Token).AsTask();
        try
        {
            await Task.WhenAny(elapsed, request).ConfigureAwait(false);
            return request.IsCompletedSuccessfully;
        }
        finally
        {
            await wake.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task RunAsync(ISourceAdapter adapter, SourceState state, CancellationToken stoppingToken)
    {
        state.Running = true;
        long? runId = null;
        try
        {
            runId = await runs.StartAsync(adapter.Name, clock.UtcNow, stoppingToken).ConfigureAwait(false);
            var result = await adapter.PullAsync(stoppingToken).ConfigureAwait(false);
            var detail = result.ItemErrors.Count == 0
                ? null
                : Redact(adapter, $"{result.ItemErrors.Count} of {result.ItemsAttempted} not pulled: {string.Join(" | ", result.ItemErrors)}");

            // Partial when some items were pulled; failed when none was.
            await runs.FinishAsync(
                runId.Value, clock.UtcNow, result.Counts, detail, stoppingToken,
                partial: detail is not null && !result.NothingPulled, result.Flags, result.Note).ConfigureAwait(false);
            if (detail is not null)
            {
                LogPartial(adapter.Name, detail);
            }

            LogCompleted(adapter.Name, result.Counts.Accepted, result.Counts.Duplicates, result.Counts.Revised, result.Counts.Missing);
            if (result.RecordedSignals.Count > 0)
            {
                _recorded.Writer.TryWrite(result.RecordedSignals);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            var message = Redact(adapter, exception.Message);
            LogFailed(adapter.Name, message);
            if (runId is { } id)
            {
                try
                {
                    await runs.FinishAsync(id, clock.UtcNow, null, message, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception recordFailure)
                {
                    // The database itself may be the fault (full disk); the engine keeps running.
                    LogRecordFailed(adapter.Name, recordFailure.Message);
                }
            }
        }
        finally
        {
            state.Running = false;
        }
    }

    /// <summary>A loop iteration failed outside a pull (schedule, options): recorded as a failed run.</summary>
    private async Task RecordFailureAsync(string source, string message)
    {
        try
        {
            var id = await runs.StartAsync(source, clock.UtcNow, CancellationToken.None).ConfigureAwait(false);
            await runs.FinishAsync(id, clock.UtcNow, null, message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception recordFailure)
        {
            LogRecordFailed(source, recordFailure.Message);
        }
    }

    /// <summary>Announces recorded ids off the adapter loops, one event for everything queued meanwhile.</summary>
    private async Task PublishLoopAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            while (await _recorded.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                var ids = new List<Guid>();
                while (_recorded.Reader.TryRead(out var batch))
                {
                    ids.AddRange(batch);
                }

                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                        .PublishAsync(new SignalBatchAccepted(CorrelationId.New(), ids), stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogPublishFailed(ids.Count, exception.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown; pending signals are classified by the classification scheduler's next run.
        }
    }

    private static string Redact(ISourceAdapter adapter, string text)
    {
        try
        {
            return adapter.Redact(text);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never log text an adapter could not redact.
            return "error text withheld: redaction failed";
        }
    }

    private static DateTimeOffset Later(DateTimeOffset now, DateTimeOffset? lastSlot) => lastSlot is { } slot && slot > now ? slot : now;

    private static TimeSpan Positive(TimeSpan span) => span > TimeSpan.Zero ? span : TimeSpan.Zero;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Source} disabled: {Reason}")]
    private partial void LogDisabled(string source, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Source} recorded {Accepted} signals ({Duplicates} duplicates, {Revised} revised values kept as first recorded, {Missing} missing values skipped)")]
    private partial void LogCompleted(string source, int accepted, int duplicates, int revised, int missing);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} pull partial: {Detail}")]
    private partial void LogPartial(string source, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Source {Source} pull failed: {Error}")]
    private partial void LogFailed(string source, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Source {Source} scheduling failed: {Error}; retrying after a back-off")]
    private partial void LogLoopFailed(string source, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Source {Source} run record could not be written: {Error}")]
    private partial void LogRecordFailed(string source, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Announcing {Count} recorded signals failed: {Error}; the classification scheduler picks them up")]
    private partial void LogPublishFailed(int count, string error);
}
