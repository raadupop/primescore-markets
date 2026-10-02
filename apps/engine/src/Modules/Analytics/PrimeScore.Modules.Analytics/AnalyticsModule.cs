using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrimeScore.Ledger;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Features;
using PrimeScore.Modules.Analytics.Storage;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IEngineSchema, ReplaySchema>();
        services.AddSingleton<ILedgerProjection, ReplayProjection>();
        services.AddSingleton<ReplayReadStore>();
        services.AddScoped<ICommandHandler<RunReplay, RunReplayAck>, RunReplayHandler>();
        services.AddScoped<IQueryHandler<GetReplay, ReplayView?>, GetReplayHandler>();
        services.AddScoped<IQueryHandler<GetReplayHistory, IReadOnlyList<ReplayView>>, GetReplayHistoryHandler>();
        services.AddScoped<IQueryHandler<GetValidationEvents, IReadOnlyList<ValidationEvent>>, GetValidationEventsHandler>();
        services.AddScoped<ICommandHandler<EvaluateValidationEvents, ValidationEvaluationAck>, EvaluateValidationEventsHandler>();
        services.AddScoped<IQueryHandler<GetValidationReport, IReadOnlyList<ValidationEventResult>>, GetValidationReportHandler>();
        services.AddScoped<IQueryHandler<GetForwardOutcomes, ForwardOutcomesReport?>, GetForwardOutcomesHandler>();
        services.AddScoped<IQueryHandler<GetCatalystRatios, CatalystRatioList>, GetCatalystRatiosHandler>();
        services.AddScoped<IQueryHandler<GetCatalystOutcomes, CatalystOutcomeReport>, GetCatalystOutcomesHandler>();
        services.AddScoped<IQueryHandler<GetPositionScenarios, PositionScenarioReport>, GetPositionScenariosHandler>();
        return services;
    }
}
