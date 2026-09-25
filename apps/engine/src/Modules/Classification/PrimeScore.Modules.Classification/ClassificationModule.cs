using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Aggregation;
using PrimeScore.Modules.Classification.Classifier;
using PrimeScore.Modules.Classification.Consensus;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Classification.Pipeline;
using PrimeScore.Modules.Classification.Storage;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Classification;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class ClassificationModule
{
    public static IServiceCollection AddClassificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<ClassifierOptions>(configuration.GetSection(ClassifierOptions.Section));
        services.Configure<ConsensusOptions>(configuration.GetSection(ConsensusOptions.Section));
        services.AddHttpClient(ClassifierOptions.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ClassifierOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddSingleton<IEngineSchema, ClassificationSchema>();
        services.AddSingleton<ILedgerProjection, AssessmentProjection>();
        services.AddSingleton<ILedgerProjection, AggregateProjection>();
        services.AddSingleton<ClassificationReadStore>();
        services.AddSingleton<ConsensusBook>();
        services.AddSingleton<ClassifierClient>();
        services.AddSingleton<ClassificationGate>();
        services.AddScoped<SignalClassifier>();
        services.AddScoped<CompositeRunner>();
        services.AddScoped<ClassificationRunner>();

        services.AddScoped<IQueryHandler<GetClassifierHealth, ClassifierHealth>, GetClassifierHealthHandler>();
        services.AddScoped<ICommandHandler<ClassifyPendingSignals, ClassifyPendingAck>, ClassifyPendingSignalsHandler>();
        services.AddScoped<IQueryHandler<GetAssessments, IReadOnlyList<AssessmentView>>, GetAssessmentsHandler>();
        services.AddScoped<IQueryHandler<GetSignalOutcomes, IReadOnlyDictionary<Guid, AssessmentView>>, GetSignalOutcomesHandler>();
        services.AddScoped<IQueryHandler<GetConsensusStatus, ConsensusStatus>, GetConsensusStatusHandler>();
        services.AddScoped<IQueryHandler<GetClassificationSummary, ClassificationSummary>, GetClassificationSummaryHandler>();
        services.AddScoped<IQueryHandler<GetComposite, CompositeView?>, GetCompositeHandler>();
        services.AddScoped<IQueryHandler<GetDislocation, DislocationView?>, GetDislocationHandler>();
        services.AddScoped<IQueryHandler<GetCompositeHistory, IReadOnlyList<DailyAggregate>>, GetCompositeHistoryHandler>();
        services.AddScoped<IQueryHandler<GetAggregateContexts, IReadOnlyList<AggregateContextView>>, GetAggregateContextsHandler>();
        services.AddScoped<IIntegrationEventHandler<SignalBatchAccepted>, SignalBatchAcceptedHandler>();
        services.AddHostedService<ClassificationScheduler>();
        return services;
    }
}
