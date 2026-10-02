using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using Dto = PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api.Controllers;

/// <summary>
/// The catalyst calendar (ADR-0011) with the 9-day/30-day ratio read before each catalyst
/// (ADR-0012). The calendar comes from the Catalysts module and the ratio from Analytics, joined
/// here by id with one ratio query per request. <c>from</c> defaults to now and <c>to</c> to 30
/// days after <c>from</c>; there is no range cap, so bulk history reads use the same endpoint.
/// </summary>
[Authorize(Policy = ApiPolicies.Read)]
public sealed class CatalystsController(
    IQueryHandler<GetCatalysts, CatalystList> catalysts,
    IQueryHandler<GetCatalyst, CatalystDetail?> catalyst,
    IQueryHandler<GetCatalystRatios, CatalystRatioList> ratios,
    IClock clock) : Dto.CatalystsControllerBase
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);

    public override async Task<ActionResult<ICollection<Dto.Catalyst>>> ListCatalysts(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Dto.CatalystFamily? family,
        CancellationToken cancellationToken = default)
    {
        var start = from ?? clock.UtcNow;
        var end = to ?? start + DefaultRange;
        if (end < start)
        {
            return ApiResults.BadRequest($"to ({end:O}) is before from ({start:O}).");
        }

        var list = await catalysts.HandleAsync(
            new GetCatalysts(start, end, family is { } requested ? CatalystDtos.ToModule(requested) : null), cancellationToken).ConfigureAwait(false);
        var readings = await ReadingsAsync(list.Catalysts.Select(view => view.CatalystId).ToArray(), cancellationToken).ConfigureAwait(false);
        return list.Catalysts.Select(view => CatalystDtos.From(view, readings.GetValueOrDefault(view.CatalystId))).ToList();
    }

    public override async Task<ActionResult<Dto.CatalystDetail>> GetCatalyst(string catalyst_id, CancellationToken cancellationToken = default)
    {
        var detail = await catalyst.HandleAsync(new GetCatalyst(catalyst_id), cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return ApiResults.NotFound($"No catalyst {catalyst_id}.");
        }

        var readings = await ReadingsAsync([detail.Catalyst.CatalystId], cancellationToken).ConfigureAwait(false);
        return CatalystDtos.From(detail, readings.GetValueOrDefault(detail.Catalyst.CatalystId));
    }

    private async Task<IReadOnlyDictionary<string, CatalystRatioReading>> ReadingsAsync(string[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
        {
            return new Dictionary<string, CatalystRatioReading>(StringComparer.Ordinal);
        }

        var list = await ratios.HandleAsync(new GetCatalystRatios(ids), cancellationToken).ConfigureAwait(false);
        return list.Readings.ToDictionary(reading => reading.CatalystId, StringComparer.Ordinal);
    }
}
