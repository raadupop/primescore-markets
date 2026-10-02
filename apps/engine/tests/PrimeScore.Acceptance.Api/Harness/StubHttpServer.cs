using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PrimeScore.Acceptance.Api.Harness;

/// <summary>
/// A loopback HTTP server standing in for an external data source, so acceptance tests never touch
/// the network. Serves registered paths from memory, answers registered redirects with 307, 404
/// otherwise, and logs every request (path and query).
/// </summary>
public sealed class StubHttpServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, (byte[] Body, string ContentType)> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _redirects = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _requests = new();
    private WebApplication _app = null!;

    private StubHttpServer()
    {
    }

    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>Path and query of every request received, in arrival order.</summary>
    public IReadOnlyList<string> Requests => [.. _requests];

    public static async Task<StubHttpServer> StartAsync()
    {
        var server = new StubHttpServer();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        server._app = builder.Build();
        server._app.Run(server.HandleAsync);
        await server._app.StartAsync().ConfigureAwait(false);
        var address = server._app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        server.BaseAddress = new Uri(address.TrimEnd('/') + "/");
        return server;
    }

    public void Serve(string path, byte[] body, string contentType = "text/csv") => _files[path] = (body, contentType);

    /// <summary>Answers <paramref name="path"/> with 307 to <paramref name="target"/>, as Cboe's CDN does.</summary>
    public void Redirect(string path, string target) => _redirects[path] = target;

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        _requests.Enqueue(path + context.Request.QueryString.Value);
        if (_redirects.TryGetValue(path, out var target))
        {
            context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
            context.Response.Headers.Location = target;
            return;
        }

        if (!_files.TryGetValue(path, out var file))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = file.ContentType;
        await context.Response.Body.WriteAsync(file.Body, context.RequestAborted).ConfigureAwait(false);
    }
}
