using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>The generic scheduler, pull command and status query over scripted adapters and a real engine database.</summary>
public sealed class SourceSchedulerTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-scheduler-tests", Guid.NewGuid().ToString("N"));
    private readonly ScriptedAdapter _first = new("First");
    private readonly ScriptedAdapter _second = new("Second");
    private readonly BlockingPublisher _publisher = new();
    private ServiceProvider _services = null!;
    private SourceScheduler? _scheduler;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(new TestClock(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration)
            .AddSingleton<ISourceAdapter>(_first)
            .AddSingleton<ISourceAdapter>(_second)
            .AddSingleton<IIntegrationEventPublisher>(_publisher);
        _services = services.BuildServiceProvider();
        await EngineDatabaseInitializer.InitializeAsync(_services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        _publisher.Release();
        if (_scheduler is not null)
        {
            await _scheduler.StopAsync(CancellationToken.None);
        }

        await _services.DisposeAsync();
        new EngineDatabase(Path.Combine(_directory, "engine.db")).ClearPool();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_pull_request_names_a_registered_enabled_source_in_any_letter_case()
    {
        _second.DisabledReason = "disabled by configuration (test)";

        var unknown = await RequestAsync("nope");
        var disabled = await RequestAsync("Second");
        var fred = await RequestAsync("FRED");
        var queued = await RequestAsync("first");

        Assert.Equal(new SourcePullAck(false, "Unknown source 'nope'."), unknown);
        Assert.Equal(new SourcePullAck(false, "disabled by configuration (test)"), disabled);
        Assert.False(fred.Queued);
        Assert.Contains("Sources:Fred:Enabled=true", fred.Reason, StringComparison.Ordinal);
        Assert.True(queued.Queued);
    }

    [Fact]
    public async Task A_requested_pull_runs_and_its_run_is_recorded()
    {
        await StartAsync();

        await RequestAsync("FIRST");

        await _first.Pulled.Task.WaitAsync(Patience, Token);
        await Eventually(async () => (await RunsAsync("First")).Any(run => run.FinishedAtMs is not null && run.Succeeded));
    }

    [Fact]
    public async Task A_startup_pull_waits_for_the_startup_pulls_of_adapters_registered_before_it()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? firstClosedWhenSecondStarted = null;
        _first.RunOnStartup = true;
        _first.Pull = async cancellationToken =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return ScriptedAdapter.Empty;
        };
        _second.RunOnStartup = true;
        _second.Pull = async _ =>
        {
            firstClosedWhenSecondStarted = (await RunsAsync("First")).Any(run => run.FinishedAtMs is not null);
            return ScriptedAdapter.Empty;
        };

        await StartAsync();
        await _first.Pulled.Task.WaitAsync(Patience, Token);
        await Task.Delay(300, Token);
        Assert.Equal(0, _second.Pulls);
        gate.SetResult();

        await _second.Pulled.Task.WaitAsync(Patience, Token);
        await Eventually(() => Task.FromResult(firstClosedWhenSecondStarted is not null));
        Assert.True(firstClosedWhenSecondStarted);
    }

    [Fact]
    public async Task Announcing_recorded_signals_does_not_delay_the_next_adapter()
    {
        var recorded = Guid.NewGuid();
        _first.RunOnStartup = true;
        _first.Pull = _ => Task.FromResult(ScriptedAdapter.Empty with { RecordedSignals = [recorded] });
        _second.RunOnStartup = true;

        await StartAsync();

        // The publisher blocks until the test ends; the second adapter's startup pull runs regardless.
        await _publisher.Called.Task.WaitAsync(Patience, Token);
        await _second.Pulled.Task.WaitAsync(Patience, Token);
        Assert.Equal([recorded], Assert.Single(_publisher.Events).SignalIds);
    }

    [Fact]
    public async Task A_throwing_adapter_is_recorded_as_failed_with_its_error_redacted_and_the_others_still_run()
    {
        _first.RunOnStartup = true;
        _first.Pull = _ => throw new InvalidOperationException("provider refused key sekret");
        _second.RunOnStartup = true;

        await StartAsync();

        await _second.Pulled.Task.WaitAsync(Patience, Token);
        await Eventually(async () => (await RunsAsync("First")).Any(run => run.FinishedAtMs is not null));
        var failed = Assert.Single(await RunsAsync("First"));
        Assert.False(failed.Succeeded);
        Assert.Equal("provider refused key ***", failed.Error);
    }

    [Fact]
    public async Task A_schedule_that_throws_is_recorded_as_a_failed_run_and_its_loop_retries_without_stopping_the_others()
    {
        _first.Schedule = new SourceSchedule([], [new PollingWindow(new TimeOnly(18, 0), new TimeOnly(8, 0), TimeSpan.Zero, SourceDays.EveryDay)]);
        _second.RunOnStartup = true;

        await StartAsync(TimeSpan.FromMilliseconds(20));

        await _second.Pulled.Task.WaitAsync(Patience, Token);
        List<SourceRunRow> finished = [];
        await Eventually(async () => (finished = [.. (await RunsAsync("First")).Where(run => run.FinishedAtMs is not null)]).Count >= 2);

        // Only finished rows: a later retry may have started its row and not yet written the error.
        Assert.All(finished, run => Assert.Contains("Polling interval must be positive", run.Error, StringComparison.Ordinal));
        Assert.Equal(0, _first.Pulls);
        Assert.False(_scheduler!.ExecuteTask!.IsCompleted);
    }

    [Fact]
    public async Task A_schedule_slot_starts_exactly_one_pull_without_a_request()
    {
        // The pinned clock reads 12:00Z = 08:00 EDT (UTC−4) on 29 September 2026; the slot is 300 ms later.
        var slot = new DateTimeOffset(2026, 9, 29, 12, 0, 0, 300, TimeSpan.Zero);
        var runtime = _services.GetRequiredService<SourceRuntime>();
        DateTimeOffset? nextRunAtDuringPull = null;
        _first.Schedule = new SourceSchedule([new DailyRun(new TimeOnly(8, 0, 0, 300), SourceDays.EveryDay)], []);
        _first.Pull = _ =>
        {
            nextRunAtDuringPull = runtime.For("First").NextRunAt;
            return Task.FromResult(ScriptedAdapter.Empty);
        };

        await StartAsync();
        await _first.Pulled.Task.WaitAsync(Patience, Token);
        await Eventually(async () => (await RunsAsync("First")).Any(run => run.FinishedAtMs is not null));

        // The clock never moves: a loop that forgot the slot it ran would run it again at once.
        await Task.Delay(1000, Token);
        Assert.Equal(1, _first.Pulls);
        Assert.True(Assert.Single(await RunsAsync("First")).Succeeded);
        Assert.Equal(slot, nextRunAtDuringPull);

        // Then the next day's slot: 08:00:00.3 EDT on 30 September.
        Assert.Equal(slot.AddDays(1), runtime.For("First").NextRunAt);
    }

    [Fact]
    public async Task A_disabled_adapter_still_reports_its_run_history_flags_note_and_coverage()
    {
        _second.DisabledReason = "disabled by configuration (test)";
        _second.Series = [new SourceSeries("S1", "test:S1", "VIX", SourceCategory.MarketData, true, "close, 16:15 New York", new Uri("https://example.test/S1"))];
        var runs = _services.GetRequiredService<SourceRunStore>();
        var started = new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero);
        var id = await runs.StartAsync("Second", started, Token);
        await runs.FinishAsync(id, started.AddMinutes(1), new SourceRunCounts(1, 2, 0, 3, 0), null, Token, flags: ["VIX 2026-09-25: disagreement"], note: "Compared 5 closes.");
        // An API row under the same identifier (recorded before its prefix was reserved) is not the adapter's coverage.
        await _services.GetRequiredService<SignalRecorder>().RecordAsync(
            [Candidate("test:S1"), Candidate("test:S1", "API", new DateTimeOffset(2026, 9, 28, 20, 15, 0, TimeSpan.Zero))], Token);

        var status = (await StatusAsync()).Single(source => source.Source == "Second");

        Assert.False(status.Enabled);
        Assert.Equal("disabled by configuration (test)", status.DisabledReason);
        Assert.Equal(started, status.LastAttemptAt);
        Assert.Equal(started.AddMinutes(1), status.LastSuccessAt);
        Assert.Equal(new SourceRunCounts(1, 2, 0, 3, 0), status.LastRun);
        Assert.Equal(["VIX 2026-09-25: disagreement"], status.Flags);
        Assert.Equal("Compared 5 closes.", status.Note);
        Assert.Equal("on request only", status.Schedule);
        Assert.Equal("scripted adapter Second", status.Description);
        var series = Assert.Single(status.Series);
        Assert.Equal(1, series.Recorded);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 20, 15, 0, TimeSpan.Zero), series.LatestObservedAt);
        Assert.Equal("test:S1", series.SourceIdentifier);
        Assert.Equal(new Uri("https://example.test/S1"), series.Url);
    }

    [Fact]
    public async Task Every_registered_adapter_has_a_status_row()
    {
        var status = await StatusAsync();

        // Registration order: the module registers Cboe before FRED; the test adds First and Second.
        Assert.Equal(["Cboe", "FRED", "First", "Second"], status.Select(source => source.Source));
    }

    private async Task StartAsync(TimeSpan? initialBackoff = null)
    {
        _scheduler = _services.GetServices<IHostedService>().OfType<SourceScheduler>().Single();
        if (initialBackoff is { } backoff)
        {
            _scheduler.InitialBackoff = backoff;
        }

        await _scheduler.StartAsync(Token);
    }

    private async Task<SourcePullAck> RequestAsync(string source)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RequestSourcePull, SourcePullAck>>().HandleAsync(new RequestSourcePull(source), Token);
    }

    private async Task<IReadOnlyList<SourceStatus>> StatusAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>>().HandleAsync(new GetSourceStatus(), Token);
    }

    private async Task<List<SourceRunRow>> RunsAsync(string source)
    {
        await using var context = _services.GetRequiredService<IngestionReadStore>().Open();
        return await context.SourceRuns.Where(run => run.Source == source).ToListAsync(CancellationToken.None);
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met in time");
            await Task.Delay(25, Token);
        }
    }

    private static SignalCandidate Candidate(string source, string provider = "Test", DateTimeOffset? observedAt = null) => new(
        SourceCategory.MarketData, source, "VIX", "IMPLIED_VOLATILITY", observedAt ?? new DateTimeOffset(2026, 9, 25, 20, 15, 0, TimeSpan.Zero), "STRUCTURED", 21.45,
        JsonDocument.Parse("""{"instrument":"VIX","value":21.45}""").RootElement.Clone(),
        new SignalProvenance(provider));

    private sealed class ScriptedAdapter(string name) : ISourceAdapter
    {
        private int _pulls;

        public static SourcePullResult Empty { get; } = new(new SourceRunCounts(0, 0, 0, 0, 0), [], [], 1, [], null);

        public string Name => name;

        public string Description => $"scripted adapter {name}";

        public string? DisabledReason { get; set; }

        public bool RunOnStartup { get; set; }

        public SourceSchedule Schedule { get; set; } = SourceSchedule.OnRequest;

        public IReadOnlyList<SourceSeries> Series { get; set; } = [];

        public Func<CancellationToken, Task<SourcePullResult>> Pull { get; set; } = _ => Task.FromResult(Empty);

        public int Pulls => Volatile.Read(ref _pulls);

        public TaskCompletionSource Pulled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SourcePullResult> PullAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _pulls);
            Pulled.TrySetResult();
            return Pull(cancellationToken);
        }

        public string Redact(string text) => text.Replace("sekret", "***", StringComparison.Ordinal);
    }

    /// <summary>A classification handler that takes as long as the test wants.</summary>
    private sealed class BlockingPublisher : IIntegrationEventPublisher
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<SignalBatchAccepted> Events { get; } = [];

        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            if (integrationEvent is SignalBatchAccepted batch)
            {
                lock (Events)
                {
                    Events.Add(batch);
                }
            }

            Called.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }
}
