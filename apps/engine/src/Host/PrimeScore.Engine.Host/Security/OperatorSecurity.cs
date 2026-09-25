using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace PrimeScore.Engine.Host.Security;

public sealed class OperatorOptions
{
    public string Name { get; set; } = "operator";

    public string PasswordHash { get; set; } = "";

    public bool Configured => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(PasswordHash);

    public string Stamp => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Name + "\n" + PasswordHash)));

    public bool SessionValid(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
        && (user.Identity.AuthenticationType == ApiTokenAuthenticationHandler.SchemeName
            || (Configured && user.FindFirstValue("operator_stamp") == Stamp
                && long.TryParse(user.FindFirstValue("operator_expires"), CultureInfo.InvariantCulture, out var expiry)
                && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
}

internal static class OperatorSecurity
{
    public const string Scheme = "OperatorCookie";
    public const string UiPolicy = "ui-read";
    public const string Selector = "UiOrToken";

    public static IServiceCollection AddOperatorSecurity(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.Configure<OperatorOptions>(configuration.GetSection("Auth:Operator"));
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = Selector;
            options.DefaultChallengeScheme = Scheme;
        }).AddPolicyScheme(Selector, Selector, options => options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? ApiTokenAuthenticationHandler.SchemeName : Scheme)
            .AddCookie(Scheme, options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
                options.Cookie.Name = "PrimeScore.Operator";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment() || environment.IsEnvironment("Testing")
                    ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.Events.OnValidatePrincipal = context =>
                {
                    var settings = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<OperatorOptions>>().CurrentValue;
                    if (context.Principal is not { } principal || !settings.SessionValid(principal)) { context.RejectPrincipal(); }
                    return Task.CompletedTask;
                };
            });
        services.AddAuthorizationBuilder().AddPolicy(UiPolicy, policy => policy.AddAuthenticationSchemes(Selector).RequireRole(ApiRoles.Read, ApiRoles.Admin));
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, OperatorAuthenticationStateProvider>();
        services.AddScoped<UiAccess>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("sign-in", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));
        });
        return services;
    }
}

internal sealed class OperatorAuthenticationStateProvider(ILoggerFactory loggerFactory, IOptionsMonitor<OperatorOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        Task.FromResult(options.CurrentValue.SessionValid(authenticationState.User));
}

internal sealed class UiAccess(AuthenticationStateProvider authentication, IOptionsMonitor<OperatorOptions> options)
{
    public async Task<string?> AdministratorAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync().ConfigureAwait(false)).User;
        return user.IsInRole(ApiRoles.Admin) && options.CurrentValue.SessionValid(user) ? user.Identity!.Name : null;
    }
}
