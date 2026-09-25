using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Features;

internal sealed class GetSignalsInObservationOrderHandler(IngestionReadStore reads) : IQueryHandler<GetSignalsInObservationOrder, SignalPage>
{
    public async Task<SignalPage> HandleAsync(GetSignalsInObservationOrder query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = reads.Open();
        var total = await context.Signals.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await context.Signals
            .OrderBy(row => row.ObservedAtMs).ThenBy(row => row.Sequence)
            .Skip(Math.Max(0, query.Skip)).Take(Math.Clamp(query.Take, 1, 2000))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new SignalPage(rows.Select(SignalViews.From).ToArray(), total);
    }
}

internal sealed class GetObservationSeriesHandler(IngestionReadStore reads) : IQueryHandler<GetObservationSeries, ObservationSeries>
{
    public async Task<ObservationSeries> HandleAsync(GetObservationSeries query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length <= 0)
        {
            return new ObservationSeries([]);
        }

        var before = query.Before.ToUnixTimeMilliseconds();
        var category = query.Category.ToString();
        await using var context = reads.Open();
        var rows = context.Signals.Where(row =>
            row.Instrument == query.Instrument && row.Category == category && row.ObservedAtMs < before && row.Value != null);
        if (query.Variant is { } variant)
        {
            rows = rows.Where(row => row.Variant == variant);
        }

        // Overlapping sources can repeat a date; read enough rows that the window still fills after collapsing them.
        var recent = await rows
            .OrderByDescending(row => row.ObservedAtMs).ThenBy(row => row.Sequence)
            .Take((query.Length * 3) + 10)
            .Select(row => new { row.ObservedAtMs, row.Variant, row.Sequence, row.Value, row.SignalId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var points = recent
            .GroupBy(row => (Date: MarketTime.NewYorkDate(DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs)), row.Variant))
            .Select(group => group.MinBy(row => row.Sequence)!)
            .OrderByDescending(row => row.ObservedAtMs).ThenBy(row => row.Sequence)
            .Take(query.Length)
            .Reverse()
            .Select(row => new ObservationPoint(DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs), row.Value!.Value, Guid.Parse(row.SignalId)))
            .ToArray();
        return new ObservationSeries(points);
    }
}
