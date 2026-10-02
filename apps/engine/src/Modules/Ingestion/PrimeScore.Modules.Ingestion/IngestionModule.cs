using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Features;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources;
using PrimeScore.Modules.Ingestion.Sources.Cboe;
using PrimeScore.Modules.Ingestion.Sources.Fred;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.Modules.Ingestion.Validation;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class IngestionModule
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IEngineSchema, IngestionSchema>();
        services.AddSingleton<ILedgerProjection, SignalProjection>();
        services.AddSingleton<IngestionReadStore>();
        services.AddSingleton<SignalRecorder>();
        services.AddSingleton<SignalValidator>();

        services.AddScoped<ICommandHandler<IngestSignals, IngestSignalsAck>, IngestSignalsHandler>();
        services.AddScoped<ICommandHandler<RequestSourcePull, SourcePullAck>, RequestSourcePullHandler>();
        services.AddScoped<IQueryHandler<GetSignals, SignalPage>, GetSignalsHandler>();
        services.AddScoped<IQueryHandler<GetSignal, SignalView?>, GetSignalHandler>();
        services.AddScoped<IQueryHandler<GetObservationSeries, ObservationSeries>, GetObservationSeriesHandler>();
        services.AddScoped<IQueryHandler<GetSignalsInObservationOrder, SignalPage>, GetSignalsInObservationOrderHandler>();
        services.AddScoped<IQueryHandler<GetSignalsById, SignalPage>, GetSignalsByIdHandler>();
        services.AddScoped<IQueryHandler<GetSignalKeysInObservationOrder, IReadOnlyList<SignalKey>>, GetSignalKeysInObservationOrderHandler>();
        services.AddScoped<IQueryHandler<GetRejections, IReadOnlyList<RejectionView>>, GetRejectionsHandler>();
        services.AddScoped<IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>, GetSourceStatusHandler>();

        services.Configure<FredOptions>(configuration.GetSection(FredOptions.Section));
        var legacyFredKey = !string.IsNullOrWhiteSpace(configuration[FredOptions.LegacyApiKey]);
        services.PostConfigure<FredOptions>(options => options.LegacyKeyConfigured = legacyFredKey);
        services.AddHttpClient(FredOptions.HttpClientName, (provider, client) =>
            {
                var baseUrl = provider.GetRequiredService<IOptions<FredOptions>>().Value.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .RemoveAllLoggers();
        services.Configure<CboeOptions>(configuration.GetSection(CboeOptions.Section));
        services.AddHttpClient(CboeOptions.HttpClientName, (provider, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, provider.GetRequiredService<IOptions<CboeOptions>>().Value.TimeoutSeconds));
                client.MaxResponseContentBufferSize = 64L * 1024 * 1024;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PrimeScoreMarkets/0.1 (Cboe index history reader)");
            })
            .RemoveAllLoggers();

        // Adapters and their dependencies are singletons (ISourceAdapter). Registration order is startup order:
        // Cboe before FRED, so FRED's startup cross-check sees the closes Cboe recorded.
        services.AddSingleton<ISourceAdapter, CboeIndexAdapter>();
        services.AddSingleton<FredClient>();
        services.AddSingleton<ISourceAdapter, FredCrossCheck>();
        services.AddSingleton<SourceRuntime>();
        services.AddSingleton<SourceRunStore>();
        services.AddHostedService<SourceScheduler>();
        return services;
    }
}
