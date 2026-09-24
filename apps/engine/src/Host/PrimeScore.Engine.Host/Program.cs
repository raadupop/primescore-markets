using PrimeScore.Engine.Host.Cli;
using PrimeScore.Engine.Host.Composition;

if (EngineCli.IsCommand(args))
{
    return await EngineCli.RunAsync(args).ConfigureAwait(false);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = EnginePaths.ContentRoot(),
});
builder.AddEngine();

var app = builder.Build();
await app.InitializeEngineAsync().ConfigureAwait(false);
app.UseEngine();
await app.RunAsync().ConfigureAwait(false);
return 0;
