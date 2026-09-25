using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PrimeScore.Engine.Host.Security;

namespace PrimeScore.Engine.Host.Api.Controllers;

public sealed class AccountController(IOptionsMonitor<OperatorOptions> options) : Controller
{
    [HttpPost("/account/login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("sign-in")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> SignIn([FromForm] string? username, [FromForm] string? password, [FromForm] string? returnUrl)
    {
        var account = options.CurrentValue;
        var valid = false;
        if (account.Configured && password is { Length: > 0 and <= 1024 })
        {
            try
            {
                valid = new PasswordHasher<OperatorOptions>().VerifyHashedPassword(account, account.PasswordHash, password)
                    != PasswordVerificationResult.Failed;
            }
            catch (FormatException) { }
        }

        if (!valid || !string.Equals(username, account.Name, StringComparison.Ordinal))
        {
            return LocalRedirect("/login?error=1");
        }

        var expires = DateTimeOffset.UtcNow.AddHours(8);
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.Name, account.Name), new Claim(ClaimTypes.Role, ApiRoles.Admin),
            new Claim("operator_stamp", account.Stamp),
            new Claim("operator_expires", expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ], OperatorSecurity.Scheme);
        await HttpContext.SignInAsync(OperatorSecurity.Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false, ExpiresUtc = expires, AllowRefresh = false,
        }).ConfigureAwait(false);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    [HttpPost("/account/logout")]
    [Authorize(Policy = OperatorSecurity.UiPolicy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutOperator()
    {
        await HttpContext.SignOutAsync(OperatorSecurity.Scheme).ConfigureAwait(false);
        return LocalRedirect("/login");
    }
}
