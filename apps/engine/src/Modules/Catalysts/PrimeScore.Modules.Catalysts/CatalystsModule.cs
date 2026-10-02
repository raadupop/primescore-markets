using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrimeScore.Ledger;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Features;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Sources;
using PrimeScore.Modules.Catalysts.Sources.Bea;
using PrimeScore.Modules.Catalysts.Sources.Bls;
using PrimeScore.Modules.Catalysts.Sources.Claims;
using PrimeScore.Modules.Catalysts.Sources.Eia;
using PrimeScore.Modules.Catalysts.Sources.Fed;
using PrimeScore.Modules.Catalysts.Sources.Opec;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Catalysts;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class CatalystsModule
{
    public static IServiceCollection AddCatalystsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IEngineSchema, CatalystsSchema>();
        services.AddSingleton<ILedgerProjection, CatalystProjection>();
        services.AddSingleton<CatalystsReadStore>();
        services.AddSingleton<CatalystRecorder>();

        services.AddScoped<IQueryHandler<GetCatalysts, CatalystList>, GetCatalystsHandler>();
        services.AddScoped<IQueryHandler<GetCatalyst, CatalystDetail?>, GetCatalystHandler>();
        services.AddScoped<ICommandHandler<ImportCatalysts, ImportCatalystsAck>, ImportCatalystsHandler>();

        // Calendar adapters, run by Ingestion's scheduler; registered after Ingestion's own, so they start after Cboe.
        services.Configure<FedCalendarOptions>(configuration.GetSection(FedCalendarOptions.Section));
        services.Configure<BlsCalendarOptions>(configuration.GetSection(BlsCalendarOptions.Section));
        services.Configure<BeaCalendarOptions>(configuration.GetSection(BeaCalendarOptions.Section));
        services.Configure<EiaCalendarOptions>(configuration.GetSection(EiaCalendarOptions.Section));
        services.Configure<ClaimsCalendarOptions>(configuration.GetSection(ClaimsCalendarOptions.Section));
        services.Configure<OpecCalendarOptions>(configuration.GetSection(OpecCalendarOptions.Section));

        // Per-request timeouts come from each adapter's options; BLS answers gzip.
        services.AddHttpClient(CalendarAdapter.HttpClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 8L * 1024 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
            .RemoveAllLoggers();
        services.AddSingleton<ISourceAdapter, FedCalendarAdapter>();
        services.AddSingleton<ISourceAdapter, BlsCalendarAdapter>();
        services.AddSingleton<ISourceAdapter, BeaCalendarAdapter>();
        services.AddSingleton<ISourceAdapter, EiaCalendarAdapter>();
        services.AddSingleton<ISourceAdapter, ClaimsCalendarAdapter>();
        services.AddSingleton<ISourceAdapter, OpecCalendarAdapter>();
        return services;
    }
}
