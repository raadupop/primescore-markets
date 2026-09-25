using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Storage;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Analytics.Features;

internal sealed class GetValidationEventsHandler : IQueryHandler<GetValidationEvents, IReadOnlyList<ValidationEvent>>
{
    public Task<IReadOnlyList<ValidationEvent>> HandleAsync(GetValidationEvents query, CancellationToken cancellationToken)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("validation-events.json")
            ?? throw new InvalidOperationException("Validation event catalog is missing.");
        using var reader = new StreamReader(resource);
        return Task.FromResult<IReadOnlyList<ValidationEvent>>(CanonicalJson.Deserialize<ValidationEvent[]>(reader.ReadToEnd()));
    }
}

internal sealed class EvaluateValidationEventsHandler(
    IQueryHandler<GetValidationEvents, IReadOnlyList<ValidationEvent>> events,
    ICommandHandler<RunReplay, RunReplayAck> run) : ICommandHandler<EvaluateValidationEvents, ValidationEvaluationAck>
{
    public async Task<ValidationEvaluationAck> HandleAsync(EvaluateValidationEvents command, CancellationToken cancellationToken)
    {
        var catalog = await events.HandleAsync(new GetValidationEvents(), cancellationToken).ConfigureAwait(false);
        foreach (var item in catalog)
        {
            var ack = await run.HandleAsync(new RunReplay(item.Label, item.From, item.To, null, command.RequestedBy,
                command.InputSequence, command.InputHash, RecordEmpty: true), cancellationToken).ConfigureAwait(false);
            if (ack.ReplayId is null)
            {
                throw new InvalidOperationException(string.Join("; ", ack.Errors));
            }
        }

        return new(catalog.Count);
    }
}

internal sealed class GetValidationReportHandler(
    IQueryHandler<GetValidationEvents, IReadOnlyList<ValidationEvent>> events,
    ReplayReadStore reads) : IQueryHandler<GetValidationReport, IReadOnlyList<ValidationEventResult>>
{
    public async Task<IReadOnlyList<ValidationEventResult>> HandleAsync(GetValidationReport query, CancellationToken cancellationToken)
    {
        var catalog = await events.HandleAsync(new GetValidationEvents(), cancellationToken).ConfigureAwait(false);
        var latest = new Dictionary<string, ReplayView>(StringComparer.Ordinal);
        await using var db = reads.Open();
        await foreach (var row in db.Replays.OrderByDescending(row => row.Sequence).AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var replay = CanonicalJson.Deserialize<ReplayView>(row.Payload);
            var item = catalog.FirstOrDefault(item => replay.EventLabel == item.Label && replay.From == item.From && replay.To == item.To);
            if (item is not null && string.IsNullOrWhiteSpace(replay.OverridesJson))
            {
                latest.TryAdd(item.Id, replay);
            }

            if (latest.Count == catalog.Count) { break; }
        }

        return catalog.Select(item => Evaluate(item, latest.GetValueOrDefault(item.Id))).ToArray();
    }

    private static ValidationEventResult Evaluate(ValidationEvent item, ReplayView? replay)
    {
        if (replay is null)
        {
            return new(item, null, "Not evaluated", "No stored baseline run. Load observations and run the validation set.");
        }

        var start = item.ExpectDeploy ? MarketTime.AddTradingDays(item.TargetDate, -1) : item.TargetDate;
        var decisions = CanonicalJson.Deserialize<DecisionView[]>(replay.DecisionsJson).Where(decision => decision.Context == "equity"
            && MarketTime.NewYorkDate(decision.AsOf) >= start && MarketTime.NewYorkDate(decision.AsOf) <= item.TargetDate).ToArray();
        if (decisions.Length == 0 || !decisions.Any(decision => decision.TopContributing.Count > 0))
        {
            return new(item, replay.ReplayId, "Not evaluable", "No classifiable equity input and reference IV in the target window; absent routes are not estimated.");
        }

        var held = item.ExpectDeploy ? decisions.Any(decision => decision.Outcome == DecisionOutcome.Deploy)
            : decisions.All(decision => Math.Abs(decision.DislocationValue) < decision.DislocationThreshold);
        var deploy = decisions.FirstOrDefault(decision => decision.Outcome == DecisionOutcome.Deploy);
        var detail = deploy is null ? "No DEPLOY in target window." : $"First DEPLOY: {MarketTime.NewYorkDate(deploy.AsOf):yyyy-MM-dd}.";
        return new(item, replay.ReplayId, held ? "Matches target" : "Misses target", detail + " Equity/VIX market-data proxy; geopolitical and unsourced macro routes absent.");
    }
}
