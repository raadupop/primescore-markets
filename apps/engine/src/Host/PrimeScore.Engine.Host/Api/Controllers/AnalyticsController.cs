using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.SharedKernel.Cqrs;
using Dto = PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api.Controllers;

/// <summary>
/// The outcomes record (ADR-0008): what the reference index did after each recorded decision,
/// computed from the ledger on read, 404 for an unknown context; and the event record of a catalyst
/// family (ADR-0013).
/// </summary>
[Authorize(Policy = ApiPolicies.Read)]
public sealed class AnalyticsController(
    IQueryHandler<GetForwardOutcomes, ForwardOutcomesReport?> outcomes,
    IQueryHandler<GetActiveSettings, SettingsVersion> settings,
    IQueryHandler<GetCatalystOutcomes, CatalystOutcomeReport> eventRecord) : Dto.AnalyticsControllerBase
{
    public override async Task<ActionResult<Dto.CatalystOutcomeReport>> GetCatalystOutcomes(
        Dto.CatalystFamily family,
        CancellationToken cancellationToken = default)
    {
        // The contract's member names are the wire names (FOMC, CPI, ...), as the Analytics query expects.
        var report = await eventRecord.HandleAsync(new GetCatalystOutcomes(family.ToString()), cancellationToken).ConfigureAwait(false);
        return OutcomeDtos.From(report, family);
    }

    public override async Task<ActionResult<Dto.ForwardOutcomesReport>> GetForwardOutcomes(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? context = "equity",
        CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(context) ? "equity" : context;
        var report = await outcomes.HandleAsync(new GetForwardOutcomes(name, from, to), cancellationToken).ConfigureAwait(false);
        if (report is not null)
        {
            return OutcomeDtos.From(report);
        }

        var active = await settings.HandleAsync(new GetActiveSettings(), cancellationToken).ConfigureAwait(false);
        return ApiResults.NotFound($"Unknown context '{name}'; configured: {string.Join(", ", active.Settings.Contexts.Select(known => known.Name))}.");
    }
}
