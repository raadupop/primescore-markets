using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Decision.Features;
using PrimeScore.Modules.Decision.Pipeline;
using PrimeScore.Modules.Decision.Storage;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Decision;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class DecisionModule
{
    public static IServiceCollection AddDecisionModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IEngineSchema, DecisionSchema>();
        services.AddSingleton<ILedgerProjection, DecisionProjection>();
        services.AddSingleton<DecisionReadStore>();
        services.AddSingleton<DecisionGate>();
        services.AddScoped<DecisionRunner>();

        services.AddScoped<ICommandHandler<MakePendingDecisions, MakePendingDecisionsAck>, MakePendingDecisionsHandler>();
        services.AddScoped<IQueryHandler<GetDecisions, IReadOnlyList<DecisionView>>, GetDecisionsHandler>();
        services.AddScoped<IQueryHandler<GetDecision, DecisionView?>, GetDecisionHandler>();
        services.AddScoped<IQueryHandler<GetDecisionOutcomes, IReadOnlyList<DecisionOutcomePoint>>, GetDecisionOutcomesHandler>();
        services.AddScoped<IQueryHandler<GetAuditEntries, IReadOnlyList<AuditEntryView>>, GetAuditEntriesHandler>();
        services.AddScoped<IIntegrationEventHandler<AggregatesRecorded>, AggregatesRecordedHandler>();
        return services;
    }
}
