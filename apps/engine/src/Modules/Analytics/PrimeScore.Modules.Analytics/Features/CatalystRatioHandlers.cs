using PrimeScore.Modules.Analytics.Catalysts;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Features;

/// <summary>
/// The 9-day/30-day ratio before each requested catalyst against its weekday-matched placebo
/// baseline (ADR-0012). Reads the whole current calendar once (the halo needs every family) and
/// only the <c>cboe:</c>-recorded VIX9D and VIX closes, so the derived figure never rests on a FRED
/// window; closes on or after the latest requested catalyst's New York date are not read. Nothing is
/// stored: the reading is a function of the ledger at request time.
/// </summary>
internal sealed class GetCatalystRatiosHandler(
    IQueryHandler<GetCatalysts, CatalystList> catalysts,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    IClock clock) : IQueryHandler<GetCatalystRatios, CatalystRatioList>
{
    /// <summary>Source prefix of the Cboe index adapter's rows (ADR-0009).</summary>
    public const string CboePrefix = "cboe:";

    public async Task<CatalystRatioList> HandleAsync(GetCatalystRatios query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var computedAt = clock.UtcNow;
        if (query.CatalystIds.Count == 0)
        {
            return new CatalystRatioList([], computedAt);
        }

        var calendar = await catalysts.HandleAsync(new GetCatalysts(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), cancellationToken).ConfigureAwait(false);
        var byId = calendar.Catalysts.ToDictionary(catalyst => catalyst.CatalystId, StringComparer.Ordinal);
        var requested = query.CatalystIds.Distinct(StringComparer.Ordinal)
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<CatalystView>()
            .ToArray();
        if (requested.Length == 0)
        {
            return new CatalystRatioList([], computedAt);
        }

        var before = MarketTime.AtNewYork(requested.Max(catalyst => catalyst.ScheduledDate), TimeOnly.MinValue);
        var nineDay = await ReadAsync("VIX9D", "IMPLIED_VOLATILITY:9D", before, cancellationToken).ConfigureAwait(false);
        var thirtyDay = await ReadAsync("VIX", "IMPLIED_VOLATILITY", before, cancellationToken).ConfigureAwait(false);
        var ratio = TermStructureRatio.FromCloses(nineDay, thirtyDay);
        var events = calendar.Catalysts
            .Where(catalyst => catalyst.Status == CatalystStatus.Scheduled)
            .Select(catalyst => new ScheduledEvent(catalyst.Family, catalyst.ScheduledDate))
            .ToArray();
        return new CatalystRatioList(
            requested.Select(catalyst => ratio.Read(catalyst.CatalystId, catalyst.Family, catalyst.ScheduledDate, events)).ToArray(),
            computedAt);
    }

    private async Task<IReadOnlyList<(DateTimeOffset ObservedAt, double Value)>> ReadAsync(
        string instrument, string variant, DateTimeOffset before, CancellationToken cancellationToken)
    {
        var points = await series.HandleAsync(
            new GetObservationSeries(instrument, SourceCategory.MarketData, variant, before, 1_000_000, SourcePrefix: CboePrefix),
            cancellationToken).ConfigureAwait(false);
        return points.Points.Select(point => (point.ObservedAt, point.Value)).ToArray();
    }
}
