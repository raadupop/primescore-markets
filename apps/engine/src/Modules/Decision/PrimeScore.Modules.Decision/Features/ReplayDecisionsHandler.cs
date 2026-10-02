using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Decision.Evaluation;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Decision.Features;

internal sealed class GetReplayDecisionsHandler(IQueryHandler<GetReplayAggregates, IReadOnlyList<AggregateRecord>> aggregates)
    : IQueryHandler<GetReplayDecisions, IReadOnlyList<DecisionView>>
{
    public async Task<IReadOnlyList<DecisionView>> HandleAsync(GetReplayDecisions query, CancellationToken cancellationToken)
    {
        var settings = CanonicalJson.Deserialize<EngineSettings>(query.SettingsJson);
        var inputs = await aggregates.HandleAsync(new GetReplayAggregates(query.From, query.To, query.MaxSequence, query.Version,
            query.SettingsJson), cancellationToken).ConfigureAwait(false);
        return inputs.Select(aggregate =>
        {
            var result = DecisionEvaluator.Evaluate(aggregate, settings.DeployConditions!);
            var composite = aggregate.Composite;
            var dislocation = aggregate.Dislocation;
            return new DecisionView(Guid.NewGuid(), 0, composite.CorrelationId, query.Version, composite.Context, result.Outcome,
                result.Scenario, composite.CompositeId, composite.Score, dislocation.DislocationId, dislocation.DislocationValue,
                dislocation.Threshold, dislocation.ReferenceInstrument, dislocation.MarketObservedIv, dislocation.SignalImpliedIv,
                composite.TriggerSignalId, result.Conditions, result.TopContributing, result.Dissenting, result.Explanation, composite.AsOf, composite.AsOf,
                dislocation.RegimePercentile);
        }).ToArray();
    }
}
