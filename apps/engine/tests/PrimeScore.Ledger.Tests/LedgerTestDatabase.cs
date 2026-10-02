using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.SharedKernel;

namespace PrimeScore.Ledger.Tests;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>A migrated engine database in a temporary directory, with the ledger services.</summary>
internal sealed class LedgerTestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private LedgerTestDatabase(string directory, ServiceProvider services, FixedClock clock)
    {
        _directory = directory;
        Services = services;
        Clock = clock;
    }

    public ServiceProvider Services { get; }

    public FixedClock Clock { get; }

    public ILedger Ledger => Services.GetRequiredService<ILedger>();

    public ILedgerVerifier Verifier => Services.GetRequiredService<ILedgerVerifier>();

    public EngineDatabase Database => Services.GetRequiredService<EngineDatabase>();

    public static async Task<LedgerTestDatabase> CreateAsync(params ILedgerProjection[] projections)
    {
        var directory = Path.Combine(Path.GetTempPath(), "primescore-ledger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Engine:DatabasePath"] = Path.Combine(directory, "engine.db") })
            .Build();
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var services = new ServiceCollection()
            .AddSingleton<IClock>(clock)
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>))
            .AddLedger(configuration);
        foreach (var projection in projections)
        {
            services.AddSingleton(projection);
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await EngineDatabaseInitializer.InitializeAsync(provider, CancellationToken.None);
        return new LedgerTestDatabase(directory, provider, clock);
    }

    /// <summary>Runs SQL directly against the file, as an attacker or a faulty tool would.</summary>
    public async Task ExecuteRawAsync(string sql)
    {
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> CountAsync(string table)
    {
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
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
            // A pooled handle can outlive the test on Windows; the temp directory is disposable.
        }
    }
}
