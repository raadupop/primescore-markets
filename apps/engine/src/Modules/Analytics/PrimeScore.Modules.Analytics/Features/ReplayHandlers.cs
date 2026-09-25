using Microsoft.EntityFrameworkCore;
using PrimeScore.Ledger;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Storage;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Analytics.Features;

internal sealed class RunReplayHandler(
    IQueryHandler<GetSettingsAtSequence, SettingsVersion?> active,
    IQueryHandler<ResolveReplaySettings, ReplaySettingsResult> resolve,
    IQueryHandler<GetSignals, SignalPage> signals,
    IQueryHandler<GetReplayDecisions, IReadOnlyList<DecisionView>> decisions,
    ILedger ledger) : ICommandHandler<RunReplay, RunReplayAck>
{
    public async Task<RunReplayAck> HandleAsync(RunReplay command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.EventLabel) || command.EventLabel.Length > 200)
        {
            return new(null, ["event_label: required, at most 200 characters"]);
        }

        if (command.From > command.To || command.From.Year < 1900 || command.To.Year >= 9999)
        {
            return new(null, ["from/to: require an ordered range between 1900 and 9998"]);
        }

        var basis = await active.HandleAsync(new GetSettingsAtSequence(command.InputSequence), cancellationToken).ConfigureAwait(false);
        if (basis is null)
        {
            return new(null, ["No configuration was recorded at the input sequence."]);
        }
        var preview = await resolve.HandleAsync(new ResolveReplaySettings(basis.Version.Value, command.OverridesJson), cancellationToken).ConfigureAwait(false);
        if (preview.Settings is null)
        {
            return new(null, preview.Errors);
        }

        var observed = await signals.HandleAsync(new GetSignals(new SignalFilter(From: command.From, To: command.To, Take: 1,
            MaxSequence: command.InputSequence)), cancellationToken).ConfigureAwait(false);
        if (observed.Total == 0 && !command.RecordEmpty)
        {
            return new(null, ["No signal data in range; load observations before replaying."]);
        }

        var settingsJson = CanonicalJson.Serialize(preview.Settings);
        var timeline = await decisions.HandleAsync(new GetReplayDecisions(command.From, command.To, command.InputSequence, basis.Version,
            settingsJson), cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid();
        var view = new ReplayView(id, 0, command.EventLabel.Trim(), command.From, command.To, basis.Version, command.InputSequence,
            command.InputHash, settingsJson, command.OverridesJson, CanonicalJson.Serialize(timeline), observed.Total, command.RequestedBy, default);
        await ledger.AppendAsync([LedgerAppend.Create(LedgerKinds.ReplayRun, id, CorrelationId.New(), basis.Version,
            $"Replay {view.EventLabel}: {timeline.Count} decisions from {observed.Total} signals", view)], cancellationToken).ConfigureAwait(false);
        return new(id, []);
    }
}

internal sealed class GetReplayHandler(ReplayReadStore reads) : IQueryHandler<GetReplay, ReplayView?>
{
    public async Task<ReplayView?> HandleAsync(GetReplay query, CancellationToken cancellationToken)
    {
        await using var db = reads.Open();
        var id = query.ReplayId.ToString("D");
        var row = await db.Replays.SingleOrDefaultAsync(row => row.ReplayId == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : CanonicalJson.Deserialize<ReplayView>(row.Payload);
    }
}

internal sealed class GetReplayHistoryHandler(ReplayReadStore reads) : IQueryHandler<GetReplayHistory, IReadOnlyList<ReplayView>>
{
    public async Task<IReadOnlyList<ReplayView>> HandleAsync(GetReplayHistory query, CancellationToken cancellationToken)
    {
        await using var db = reads.Open();
        var rows = await db.Replays.OrderByDescending(row => row.Sequence).Take(Math.Clamp(query.Take, 1, 100))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => CanonicalJson.Deserialize<ReplayView>(row.Payload)).ToArray();
    }
}
