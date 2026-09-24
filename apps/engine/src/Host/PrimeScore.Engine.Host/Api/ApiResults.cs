using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api;

/// <summary>Contract-shaped error bodies (schema <c>ErrorResponse</c>).</summary>
internal static class ApiResults
{
    /// <summary>The wire format for bodies written outside MVC (authentication, exception handler).</summary>
    public static readonly JsonSerializerOptions WireOptions = CreateWireOptions();

    public static ErrorResponse Error(string error, string message, IEnumerable<string>? details = null) => new()
    {
        Error = error,
        Message = message,
        Details = details?.ToList(),
    };

    /// <summary>501 for Milestone B capabilities; the body states why (brief §3).</summary>
    public static ObjectResult NotImplemented(string reason) =>
        new(Error("not_implemented", reason)) { StatusCode = StatusCodes.Status501NotImplemented };

    /// <summary>501 for v1 endpoints whose milestone has not been built yet.</summary>
    public static ObjectResult NotYetBuilt(string milestone, string requirements) =>
        NotImplemented($"Not built yet: planned for v1 milestone {milestone} (SRS {requirements}).");

    public static ObjectResult BadRequest(string message, IEnumerable<string>? details = null) =>
        new(Error("bad_request", message, details)) { StatusCode = StatusCodes.Status400BadRequest };

    public static ObjectResult NotFound(string message) =>
        new(Error("not_found", message)) { StatusCode = StatusCodes.Status404NotFound };

    /// <summary>
    /// Unhandled failures under <c>/api</c> become a 500 <c>ErrorResponse</c> without internals;
    /// the exception goes to the log.
    /// </summary>
    public static WebApplication UseApiExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PrimeScore.Api");
            if (feature?.Error is { } exception)
            {
                logger.LogError(exception, "Unhandled API failure on {Path}", context.Request.Path.Value);
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                await context.Response.WriteAsJsonAsync(
                    Error("internal_error", "The engine failed to process this request; see the engine log."),
                    WireOptions).ConfigureAwait(false);
            }
        }));
        return app;
    }

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
