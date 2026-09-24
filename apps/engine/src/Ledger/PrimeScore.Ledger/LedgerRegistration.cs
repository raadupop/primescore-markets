using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrimeScore.Ledger.Storage;

namespace PrimeScore.Ledger;

public static class LedgerRegistration
{
    /// <summary>
    /// Registers the database (<c>Engine:DatabasePath</c>, relative to the content root) and the
    /// ledger services. Module projections register as <see cref="ILedgerProjection"/> singletons.
    /// </summary>
    public static IServiceCollection AddLedger(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton(provider =>
        {
            var configured = configuration["Engine:DatabasePath"];
            if (string.IsNullOrWhiteSpace(configured))
            {
                throw new InvalidOperationException("Engine:DatabasePath is not configured.");
            }

            var baseDirectory = provider.GetService<IHostEnvironment>()?.ContentRootPath ?? AppContext.BaseDirectory;
            return new EngineDatabase(Path.GetFullPath(configured, baseDirectory));
        });
        services.AddSingleton<SqliteLedger>();
        services.AddSingleton<ILedger>(provider => provider.GetRequiredService<SqliteLedger>());
        services.AddSingleton<ILedgerVerifier>(provider => provider.GetRequiredService<SqliteLedger>());
        services.AddSingleton<ILedgerStatusQuery>(provider => provider.GetRequiredService<SqliteLedger>());
        services.AddSingleton<IPipelineLogQuery>(provider => provider.GetRequiredService<SqliteLedger>());
        services.AddSingleton<ILedgerAuditQuery>(provider => provider.GetRequiredService<SqliteLedger>());
        services.AddSingleton<IEngineSchema, LedgerSchema>();
        return services;
    }
}

internal sealed class LedgerSchema(EngineDatabase database) : IEngineSchema
{
    public string Name => "ledger";

    public int Order => 0;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = new LedgerDbContext(database.ContextOptions<LedgerDbContext>(LedgerDbContext.HistoryTable));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
