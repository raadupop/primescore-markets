using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using PrimeScore.Acceptance.Api.Harness;

namespace PrimeScore.Acceptance.Api;

public sealed class UiSecurityTests
{
    [Fact]
    public async Task SEC_001_UI_requires_sign_in_and_cookie_sessions_cannot_authenticate_the_bearer_API()
    {
        const string password = "Acceptance-only-password-2026!";
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string>
        {
            ["Auth__Operator__Name"] = "operator",
            ["Auth__Operator__PasswordHash"] = new PasswordHasher<object>().HashPassword(new object(), password),
        });
        var token = TestContext.Current.CancellationToken;
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var browser = new HttpClient(handler) { BaseAddress = new Uri(engine.ApiBase, "/") };
        using var anonymous = await browser.GetAsync("configuration", token);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("login", anonymous.Headers.Location!.ToString(), StringComparison.Ordinal);

        using var csrfMissing = await browser.PostAsync("account/login", Form(password, null), token);
        Assert.Equal(HttpStatusCode.BadRequest, csrfMissing.StatusCode);
        var csrf = await AntiforgeryAsync(browser, token);
        using var wrong = await browser.PostAsync("account/login", Form("wrong", csrf), token);
        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        Assert.Contains("error=1", wrong.Headers.Location!.ToString(), StringComparison.Ordinal);

        csrf = await AntiforgeryAsync(browser, token);
        using var login = await browser.PostAsync("account/login", Form(password, csrf), token);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/configuration", login.Headers.Location!.ToString());
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        using var page = await browser.GetAsync("configuration", token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Save configuration", await page.Content.ReadAsStringAsync(token), StringComparison.Ordinal);
        using var api = await browser.GetAsync("api/health", token);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);

        csrf = await AntiforgeryAsync(browser, token);
        using var logout = await browser.PostAsync("account/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrf,
        }), token);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var signedOut = await browser.GetAsync("configuration", token);
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
    }

    [Fact]
    public async Task SEC_001_READ_ui_cannot_see_administrator_mutation_controls()
    {
        await using var engine = await RunningEngine.StartAsync();
        using var read = engine.Http(Role.Read);
        var token = TestContext.Current.CancellationToken;
        var configuration = await read.GetStringAsync(new Uri(engine.ApiBase, "/configuration"), token);
        Assert.DoesNotContain("Save configuration", configuration, StringComparison.Ordinal);
        var replay = await read.GetStringAsync(new Uri(engine.ApiBase, "/replay"), token);
        Assert.DoesNotContain("Run replay</button>", replay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_001_login_does_not_redirect_outside_the_application_and_limits_attempts()
    {
        const string password = "Acceptance-only-password-2026!";
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string>
        {
            ["Auth__Operator__Name"] = "operator",
            ["Auth__Operator__PasswordHash"] = new PasswordHasher<object>().HashPassword(new object(), password),
        });
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var browser = new HttpClient(handler) { BaseAddress = new Uri(engine.ApiBase, "/") };
        var token = TestContext.Current.CancellationToken;
        var csrf = await AntiforgeryAsync(browser, token);
        using var login = await browser.PostAsync("account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "operator", ["password"] = password, ["__RequestVerificationToken"] = csrf,
            ["returnUrl"] = "https://example.invalid/",
        }), token);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.ToString());
        csrf = await AntiforgeryAsync(browser, token);
        for (var attempt = 0; attempt < 9; attempt++)
        {
            using var response = await browser.PostAsync("account/login", Form("wrong", csrf), token);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var limited = await browser.PostAsync("account/login", Form("wrong", csrf), token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private static FormUrlEncodedContent Form(string password, string? csrf)
    {
        var values = new Dictionary<string, string> { ["username"] = "operator", ["password"] = password, ["returnUrl"] = "/configuration" };
        if (csrf is not null) { values["__RequestVerificationToken"] = csrf; }
        return new(values);
    }

    private static async Task<string> AntiforgeryAsync(HttpClient browser, CancellationToken token)
    {
        var page = await browser.GetStringAsync("login", token);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The sign-in page must carry a request-verification token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
