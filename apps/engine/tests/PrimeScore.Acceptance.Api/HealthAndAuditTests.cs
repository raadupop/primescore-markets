using System.Net;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

public sealed class HealthAndAuditTests(EngineFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_reports_the_ledger_and_a_reachable_classifier_whose_own_windows_are_not_bootstrapped()
    {
        await using var engine = await RunningEngine.StartAsync();

        var health = await engine.Client(Role.Read).GetHealthAsync(Token);

        Assert.Equal(EngineHealthStatus.Ok, health.Status);
        Assert.False(string.IsNullOrWhiteSpace(health.Engine_version));
        Assert.Equal(0, health.Ledger.Entries);
        Assert.Equal(0, health.Ledger.Head_sequence);
        Assert.Equal(ClassifierStatus.Reachable, health.Classifier.Status);

        // The classifier runs with provider bootstrap disabled, so it reports not_ready itself.
        Assert.False(health.Classifier.Ready);
    }

    [Fact]
    public async Task Health_is_degraded_and_names_the_cause_when_the_classifier_is_unreachable()
    {
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string>
        {
            ["Classifier__BaseUrl"] = "http://127.0.0.1:1/",
        });

        var health = await engine.Client(Role.Read).GetHealthAsync(Token);

        Assert.Equal(EngineHealthStatus.Degraded, health.Status);
        Assert.Equal(ClassifierStatus.Unreachable, health.Classifier.Status);
        Assert.False(string.IsNullOrWhiteSpace(health.Classifier.Detail));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Audit_trail_rejects_modification_with_405_even_for_admin(string method)
    {
        using var http = fixture.Engine.Http(Role.Admin);

        using var response = await http.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri("audit", UriKind.Relative)), Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains("GET", response.Content.Headers.Allow);
    }

    [Fact]
    public async Task Logs_for_an_unknown_correlation_id_are_an_empty_list()
    {
        var logs = await fixture.Engine.Client(Role.Read).GetLogsByCorrelationAsync(Guid.NewGuid(), Token);

        Assert.Empty(logs);
    }
}
