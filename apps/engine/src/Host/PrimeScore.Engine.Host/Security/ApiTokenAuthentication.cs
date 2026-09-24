using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PrimeScore.Engine.Host.Api;

namespace PrimeScore.Engine.Host.Security;

internal static class ApiRoles
{
    public const string Read = "READ";
    public const string Admin = "ADMIN";
}

internal static class ApiPolicies
{
    /// <summary>All GET endpoints (SRS SEC-001).</summary>
    public const string Read = "api-read";

    /// <summary>Ingestion, configuration and replay (SRS SEC-001).</summary>
    public const string Admin = "api-admin";
}

/// <summary>
/// Bound from <c>Auth</c>. Tokens are never stored: only their SHA-256, supplied through
/// user-secrets or environment variables (SRS SEC-002).
/// </summary>
internal sealed class ApiTokenOptions
{
    public const string Section = "Auth";

    public List<ApiTokenEntry> ApiTokens { get; set; } = [];
}

internal sealed class ApiTokenEntry
{
    public string Name { get; set; } = "";

    public string Role { get; set; } = "";

    /// <summary>Lowercase or uppercase hex SHA-256 of the UTF-8 token.</summary>
    public string Sha256 { get; set; } = "";
}

internal sealed class ApiTokenOptionsValidator : IValidateOptions<ApiTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiTokenOptions options)
    {
        var failures = new List<string>();
        foreach (var (entry, index) in options.ApiTokens.Select((entry, index) => (entry, index)))
        {
            if (entry.Role is not (ApiRoles.Read or ApiRoles.Admin))
            {
                failures.Add($"Auth:ApiTokens:{index}:Role must be {ApiRoles.Read} or {ApiRoles.Admin}.");
            }

            if (entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit))
            {
                failures.Add($"Auth:ApiTokens:{index}:Sha256 must be 64 hex characters.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Bearer tokens mapped to READ or ADMIN; unknown tokens get 401, wrong role 403.</summary>
internal sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<ApiTokenOptions> tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiToken";

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || header.Length == prefix.Length)
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header is not a bearer token."));
        }

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()));
        foreach (var entry in tokens.CurrentValue.ApiTokens)
        {
            if (CryptographicOperations.FixedTimeEquals(presented, Convert.FromHexString(entry.Sha256)))
            {
                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, entry.Name), new Claim(ClaimTypes.Role, entry.Role)],
                    SchemeName);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }
        }

        return Task.FromResult(AuthenticateResult.Fail("Unknown API token."));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Response.WriteAsJsonAsync(ApiResults.Error("unauthorized", "A valid bearer token is required (SRS SEC-001)."), ApiResults.WireOptions).ConfigureAwait(false);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(ApiResults.Error("forbidden", "This operation requires the ADMIN role (SRS SEC-001)."), ApiResults.WireOptions).ConfigureAwait(false);
    }
}

internal static class ApiSecurityRegistration
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiTokenOptions>().Bind(configuration.GetSection(ApiTokenOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiTokenOptions>, ApiTokenOptionsValidator>();
        services.AddAuthentication(ApiTokenAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(ApiPolicies.Read, policy => policy
                .AddAuthenticationSchemes(ApiTokenAuthenticationHandler.SchemeName)
                .RequireRole(ApiRoles.Read, ApiRoles.Admin))
            .AddPolicy(ApiPolicies.Admin, policy => policy
                .AddAuthenticationSchemes(ApiTokenAuthenticationHandler.SchemeName)
                .RequireRole(ApiRoles.Admin));
        return services;
    }
}
