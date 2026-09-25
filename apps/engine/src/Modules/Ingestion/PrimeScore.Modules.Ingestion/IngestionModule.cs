using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Features;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources;
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
        services.AddScoped<IQueryHandler<GetRejections, IReadOnlyList<RejectionView>>, GetRejectionsHandler>();
        services.AddScoped<IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>, GetSourceStatusHandler>();

        services.Configure<FredOptions>(configuration.GetSection(FredOptions.Section));
        services.AddHttpClient(FredOptions.HttpClientName, (provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<FredOptions>>().Value.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .RemoveAllLoggers();
        services.AddScoped<FredClient>();
        services.AddScoped<FredPuller>();
        services.AddSingleton<SourcePullQueue>();
        services.AddSingleton<SourceRunStore>();
        services.AddHostedService<SourceScheduler>();
        return services;
    }
}
