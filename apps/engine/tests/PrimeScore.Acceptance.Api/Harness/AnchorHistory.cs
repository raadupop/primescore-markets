using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api.Harness;

/// <summary>
/// Loads a classifier anchor fixture's sourced history and its event close through
/// <c>POST /admin/signals</c>, the only way acceptance tests put data into the engine.
/// </summary>
public static class AnchorHistory
{
    /// <summary>
    /// Values: the fixture's <c>long_horizon_window</c> (FRED VIXCLS as the fixture cites).
    /// Timestamps: consecutive business days ending <paramref name="lastHistoryDay"/> at 16:15
    /// New York standard time, used only to order the closes; the event follows.
    /// </summary>
    /// <returns>The recorded signals, history first and the event last.</returns>
    public static async Task<IngestSignalsResponse> LoadAsync(
        RunningEngine engine, string fixture, DateOnly lastHistoryDay, double eventValue, DateTimeOffset eventAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var anchor = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.ClassifierDirectory, "tests", "acceptance", "fixtures", fixture), cancellationToken))!;
        var symbol = anchor["symbol"]!.GetValue<string>();
        var history = anchor["long_horizon_window"]!.AsArray().Select(value => value!.GetValue<double>()).ToArray();
        var days = BusinessDaysEnding(lastHistoryDay, history.Length);
        var signals = history.Select((value, index) => (JsonNode)SignalDocuments.MarketData(symbol, value, days[index], source: "fixture:VIXCLS")).ToList();
        signals.Add(SignalDocuments.MarketData(symbol, eventValue, eventAt, source: "fixture:VIXCLS"));
        return await PostAsync(engine, new JsonObject { ["signals"] = new JsonArray([.. signals]) }, cancellationToken);
    }

    public static async Task<IngestSignalsResponse> PostAsync(RunningEngine engine, JsonObject batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(batch);
        using var http = engine.Http(Role.Admin);
        using var response = await http.PostAsync(new Uri("admin/signals", UriKind.Relative),
            new StringContent(batch.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<IngestSignalsResponse>(ApiJsonOptions.Value, cancellationToken))!;
        Assert.Equal(0, body.Rejected_count);
        return body;
    }

    public static DateTimeOffset[] BusinessDaysEnding(DateOnly last, int count)
    {
        var days = new List<DateTimeOffset>(count);
        for (var date = last; days.Count < count; date = date.AddDays(-1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(new DateTimeOffset(date.ToDateTime(new TimeOnly(21, 15)), TimeSpan.Zero));
            }
        }

        days.Reverse();
        return [.. days];
    }
}
