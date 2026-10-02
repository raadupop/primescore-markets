using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Catalysts.Tests;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

/// <summary>A real engine database with the ledger and the Catalysts module, clock pinned at 2026-09-29 12:00 UTC.</summary>
public abstract class CatalystDatabase : IAsyncLifetime
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-catalysts-tests", Guid.NewGuid().ToString("N"));

    internal ServiceProvider Services { get; private set; } = null!;

    internal CatalystRecorder Recorder => Services.GetRequiredService<CatalystRecorder>();

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(new FixedClock(Now))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddCatalystsModule(configuration);
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        await EngineDatabaseInitializer.InitializeAsync(Services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        new EngineDatabase(Path.Combine(_directory, "engine.db")).ClearPool();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Test-specific registrations applied after the module's (e.g. a stub HTTP handler).</summary>
    private protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    internal Task<RecordTally> RecordAsync(params CalendarSnapshot[] snapshots) => Recorder.RecordAsync(snapshots, force: false, Token);

    internal async Task<long> HeadAsync() =>
        (await Services.GetRequiredService<ILedgerStatusQuery>().GetAsync(Token)).HeadSequence;

    internal async Task<LedgerRecord> EntryAsync(long sequence) =>
        await Services.GetRequiredService<ILedgerAuditQuery>().BySequenceAsync(sequence, Token)
        ?? throw new InvalidOperationException($"No ledger entry {sequence}.");

    internal async Task<CatalystDetail> DetailAsync(string id) =>
        await QueryAsync<GetCatalyst, CatalystDetail?>(new GetCatalyst(id)) ?? throw new InvalidOperationException($"No catalyst {id}.");

    internal async Task<IReadOnlyList<CatalystView>> ListAsync(DateTimeOffset from, DateTimeOffset to, CatalystFamily? family = null) =>
        (await QueryAsync<GetCatalysts, CatalystList>(new GetCatalysts(from, to, family))).Catalysts;

    internal async Task<TResult> QueryAsync<TQuery, TResult>(TQuery query)
        where TQuery : IQuery<TResult>
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync(query, Token);
    }

    internal async Task<ImportCatalystsAck> ImportAsync(string csv, bool force = false)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ImportCatalysts, ImportCatalystsAck>>()
            .HandleAsync(new ImportCatalysts("catalysts.csv", csv, "ABCDEF0123", "unit-test", force), Token);
    }

    /// <summary>A New York wall-clock instant.</summary>
    internal static DateTimeOffset At(int year, int month, int day, int hour = 0, int minute = 0) =>
        MarketTime.AtNewYork(new DateOnly(year, month, day), new TimeOnly(hour, minute));

    internal static CalendarSnapshot Snapshot(
        string adapter, CatalystSourceKind kind, CatalystFamily family, params ObservedCatalyst[] rows) =>
        new(adapter, kind, $"https://example.test/{adapter}/{kind}", Now.AddHours(-1), "0123abcd", [family], null, null,
            family == CatalystFamily.Fomc ? 21 : 0, rows);

    internal static ObservedCatalyst Release(CatalystFamily family, string? key, DateTimeOffset at, bool timeAnnounced = true) =>
        new(family, at, timeAnnounced, family.ToString(), null, key, null, false, CatalystStatus.Scheduled, null);
}
