using System.Text.Json;

namespace PrimeScore.Acceptance.Api.Harness;

/// <summary>
/// Waits on source adapter runs through the read-only CLI <c>sources</c> verb, the black-box view of
/// the stored run records (the engine keeps running; the verb reads the same database).
/// </summary>
internal static class SourceRuns
{
    /// <summary>Well inside the gate's 4-minute hang timeout, so a test with several waits still fails with a message.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Polls until <paramref name="source"/> has a stored successful run, later than
    /// <paramref name="after"/> when given (a run after a restart), and returns its status row.
    /// </summary>
    public static async Task<JsonElement> WaitForSuccessAsync(RunningEngine engine, string source, DateTimeOffset? after = null)
    {
        var deadline = DateTime.UtcNow + Timeout;
        var last = "";
        while (DateTime.UtcNow < deadline)
        {
            var (exitCode, output, standardOutput) = await engine.RunEngineCommandAsync("sources");
            last = output;
            if (exitCode == 0
                && Parse(standardOutput).EnumerateArray().SingleOrDefault(row => row.GetProperty("source").GetString() == source) is { ValueKind: JsonValueKind.Object } status
                && status.GetProperty("last_success_at") is { ValueKind: JsonValueKind.String } success
                && (after is null || success.GetDateTimeOffset() > after))
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"{source} recorded no successful run{(after is null ? "" : $" after {after:O}")} within {Timeout.TotalSeconds} s. Last output:\n{last}\nEngine log: {engine.EngineLogPath}");
    }

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
