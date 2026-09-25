using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Sources.Fred;

/// <param name="SeriesErrors">Series that could not be pulled, with the provider's reason; the others were recorded.</param>
/// <param name="SeriesAttempted">Catalogued series; when every one failed the run failed.</param>
internal sealed record FredPullResult(SourceRunCounts Counts, IReadOnlyList<Guid> Recorded, IReadOnlyList<string> SeriesErrors, int SeriesAttempted)
{
    public bool NothingPulled => SeriesAttempted > 0 && SeriesErrors.Count >= SeriesAttempted;
}

/// <summary>
/// Pulls every catalogued FRED series from its newest recorded observation (or the backfill
/// start) and records the observations as signals. Missing values are skipped, never filled.
/// </summary>
internal sealed class FredPuller(
    FredClient client,
    SignalRecorder recorder,
    IndicatorRegistry registry,
    IOptions<FredOptions> options,
    IngestionReadStore reads)
{
    public async Task<FredPullResult> PullAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var (accepted, duplicates, revised, missing) = (0, 0, 0, 0);
        var recorded = new List<Guid>();
        var errors = new List<string>();
        var catalog = FredSeriesCatalog.Build(registry, settings);
        foreach (var series in catalog)
        {
            var (fetchFrom, emitFrom, backfill) = await FetchStartAsync(series, settings, cancellationToken).ConfigureAwait(false);
            FredResponse response;
            try
            {
                response = await client.GetObservationsAsync(
                    series.SeriesId, fetchFrom, series.Timing == FredTiming.InitialRelease, allowCache: backfill, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One unavailable series (an unverified mapping, a timeout, a malformed body) must not block the others.
                errors.Add(client.Redact(exception is InvalidOperationException ? exception.Message : $"FRED {series.SeriesId}: {exception.Message}"));
                continue;
            }

            var (candidates, skipped) = Candidates(series, response.Observations, response.RetrievedAt, emitFrom);
            missing += skipped;
            var outcomes = await recorder.RecordAsync(candidates, cancellationToken).ConfigureAwait(false);
            foreach (var outcome in outcomes)
            {
                switch (outcome.Status)
                {
                    case RecordStatus.Recorded:
                        accepted++;
                        recorded.Add(outcome.Id);
                        break;
                    case RecordStatus.Duplicate:
                        duplicates++;
                        break;
                    case RecordStatus.Revised:
                        revised++;
                        break;
                }
            }
        }

        return new FredPullResult(new SourceRunCounts(accepted, duplicates, revised, missing, Rejected: 0), recorded, errors, catalog.Count);
    }

    /// <summary>
    /// Signals for one series, from observations dated on or after <paramref name="emitFrom"/>;
    /// earlier ones were fetched only as year-over-year bases. The count is emitted-range
    /// observations FRED reports without a value or without a comparison base.
    /// </summary>
    internal static (IReadOnlyList<object> Candidates, int Missing) Candidates(
        FredSeries series,
        IReadOnlyList<FredObservation> observations,
        DateTimeOffset retrievedAt,
        DateOnly emitFrom)
    {
        var candidates = new List<object>(observations.Count);
        var missing = 0;
        var byDate = observations.Where(observation => observation.Value is not null)
            .GroupBy(observation => observation.Date)
            .ToDictionary(group => group.Key, group => group.First());
        double? previous = null;
        foreach (var observation in observations.OrderBy(observation => observation.Date))
        {
            if (observation.Date < emitFrom)
            {
                continue;
            }

            if (observation.Value is not { } raw)
            {
                missing++;
                continue;
            }

            double value = raw;
            string? derivation = null;
            if (series.DeriveYearOverYear)
            {
                if (!byDate.TryGetValue(observation.Date.AddMonths(-12), out var baseObservation) || baseObservation.Value is not { } baseValue || baseValue == 0)
                {
                    missing++;
                    continue;
                }

                value = Math.Round(100.0 * ((raw / baseValue) - 1.0), 4);
                derivation = "Year-over-year % change from first releases: 100 × (index_t / index_t−12 − 1). "
                    + "Seasonally adjusted series; published headline YoY is usually quoted on the unadjusted index.";
            }

            var observedAt = series.Timing == FredTiming.InitialRelease
                ? MarketTime.AtNewYork(observation.RealtimeStart, series.NewYorkTime)
                : MarketTime.AtNewYork(observation.Date, series.NewYorkTime);
            var provenance = new SignalProvenance(
                "FRED",
                SeriesId: series.SeriesId,
                Url: series.SourceUrl.ToString(),
                RetrievedAt: retrievedAt,
                FirstReleased: series.Timing == FredTiming.InitialRelease ? observation.RealtimeStart : null,
                Derivation: derivation,
                MappingVerified: series.Verified,
                Note: series.TimingDescription);
            candidates.Add(new SignalCandidate(
                series.Category, series.SourceIdentifier, series.Instrument, Variant(series, observation), observedAt, "STRUCTURED", value,
                Payload(series, observation, value, previous, observedAt), provenance));
            previous = value;
        }

        return (candidates, missing);
    }

    /// <summary>
    /// Release series carry their reference period: several periods can share a first-release
    /// date (a series' first vintage publishes its whole history at once), and each is its own fact.
    /// </summary>
    internal static string Variant(FredSeries series, FredObservation observation) => series.Category switch
    {
        SourceCategory.MarketData => "IMPLIED_VOLATILITY",
        SourceCategory.Macroeconomic => $"{series.Kind}@{observation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
        _ => "basket_observation",
    };

    private static JsonElement Payload(FredSeries series, FredObservation observation, double value, double? previous, DateTimeOffset observedAt)
    {
        var timestamp = observedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Dictionary<string, object?> payload = series.Category switch
        {
            SourceCategory.MarketData => new()
            {
                ["asset_class"] = series.Kind,
                ["instrument"] = series.Instrument,
                ["metric_type"] = "IMPLIED_VOLATILITY",
                ["value"] = value,
                ["unit"] = series.Unit,
                ["observed_at"] = timestamp,
            },
            SourceCategory.Macroeconomic => new()
            {
                ["indicator_type"] = series.Kind,
                ["region"] = "US",
                ["value"] = value,
                ["prior_value"] = previous,
                ["release_date"] = timestamp,
                ["reference_period"] = observation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["unit"] = series.Unit,
            },
            _ => new()
            {
                ["kind"] = "basket_observation",
                ["instrument"] = series.Instrument,
                ["value"] = value,
                ["unit"] = series.Unit,
                ["observation_date"] = observation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
        };
        return JsonSerializer.SerializeToElement(payload.Where(pair => pair.Value is not null).ToDictionary());
    }

    /// <summary>
    /// Resumes from the newest observation this adapter recorded for the series (rows from other
    /// providers never move it), or backfills from <c>Fred:BackfillStart</c>.
    /// </summary>
    private async Task<(DateOnly FetchFrom, DateOnly EmitFrom, bool Backfill)> FetchStartAsync(
        FredSeries series, FredOptions settings, CancellationToken cancellationToken)
    {
        await using var context = reads.Open();
        var latest = await context.Signals
            .Where(row => row.Provider == FredOptions.SourceName && row.SourceIdentifier == series.SourceIdentifier)
            .MaxAsync(row => (long?)row.ObservedAtMs, cancellationToken).ConfigureAwait(false);
        var from = latest is { } ms
            ? MarketTime.NewYorkDate(DateTimeOffset.FromUnixTimeMilliseconds(ms)).AddDays(-settings.OverlapDays)
            : settings.BackfillStart;
        if (series.Timing == FredTiming.InitialRelease && latest is not null)
        {
            // Stamps are release dates; the observation (reference) date runs up to ~6 weeks earlier.
            from = from.AddDays(-45);
        }

        return (series.DeriveYearOverYear ? from.AddMonths(-13) : from, from, latest is null);
    }
}
