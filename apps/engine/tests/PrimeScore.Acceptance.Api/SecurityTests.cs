using System.Net;
using System.Net.Http.Json;
using System.Text;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

/// <summary>SRS SEC-001: every endpoint requires authentication; ingestion, configuration and replay require ADMIN.</summary>
public sealed class SecurityTests(EngineFixture fixture)
{
    private static readonly string AnyId = Guid.NewGuid().ToString("D");

    /// <summary>Every operation in doc/PrimeScore-API-v1.yaml.</summary>
    public static readonly TheoryData<string, string> AllOperations = new()
    {
        { "GET", "health" },
        { "POST", "admin/signals" },
        { "GET", "classification/composite" },
        { "GET", "classification/assessments" },
        { "GET", "classification/dislocation" },
        { "GET", "decisions" },
        { "GET", $"decisions/{AnyId}" },
        { "GET", "positions" },
        { "GET", $"positions/{AnyId}" },
        { "GET", "exits" },
        { "GET", "risk/status" },
        { "GET", "audit" },
        { "PUT", "audit" },
        { "PATCH", "audit" },
        { "DELETE", "audit" },
        { "GET", $"logs?correlation_id={AnyId}" },
        { "PUT", "config/weighting-scheme" },
        { "PUT", "config/deploy-conditions" },
        { "PUT", "config/dislocation-threshold" },
        { "PUT", "config/risk-limits" },
        { "PUT", "config/holding-period" },
        { "PUT", "config/execution-mode" },
        { "PUT", "config/approval-threshold" },
        { "POST", "replay" },
    };

    /// <summary>Operations the contract reserves for ADMIN.</summary>
    public static readonly TheoryData<string, string> AdminOperations = new()
    {
        { "POST", "admin/signals" },
        { "PUT", "config/weighting-scheme" },
        { "PUT", "config/deploy-conditions" },
        { "PUT", "config/dislocation-threshold" },
        { "PUT", "config/risk-limits" },
        { "PUT", "config/holding-period" },
        { "PUT", "config/execution-mode" },
        { "PUT", "config/approval-threshold" },
        { "POST", "replay" },
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task Unauthenticated_call_is_rejected_with_401_and_an_error_body(string method, string path)
    {
        using var http = fixture.Engine.Http(role: null);

        using var response = await http.SendAsync(Request(method, path), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(Token);
        Assert.Equal("unauthorized", error!.Error);
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task Unknown_bearer_token_is_rejected_with_401(string method, string path)
    {
        using var http = fixture.Engine.Http(role: null);
        var request = Request(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-configured-token");

        using var response = await http.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOperations))]
    public async Task Read_token_calling_an_admin_operation_is_rejected_with_403(string method, string path)
    {
        using var http = fixture.Engine.Http(Role.Read);

        using var response = await http.SendAsync(Request(method, path), Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(Token);
        Assert.Equal("forbidden", error!.Error);
    }

    [Fact]
    public async Task Error_bodies_omit_absent_optional_fields_rather_than_sending_null()
    {
        using var http = fixture.Engine.Http(role: null);

        using var response = await http.GetAsync(new Uri("health", UriKind.Relative), Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.DoesNotContain("null", body, StringComparison.Ordinal);
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }
}
