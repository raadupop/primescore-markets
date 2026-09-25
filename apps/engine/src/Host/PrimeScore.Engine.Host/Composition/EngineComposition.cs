using PrimeScore.Api.Contracts;
using PrimeScore.Engine.Host.Api;
using PrimeScore.Engine.Host.Components;
using PrimeScore.Engine.Host.Health;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Ledger;
using PrimeScore.Modules.Analytics;
using PrimeScore.Modules.Classification;
using PrimeScore.Modules.Configuration;
using PrimeScore.Modules.Decision;
using PrimeScore.Modules.Ingestion;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Engine.Host.Composition;

/// <summary>
/// The composition root: the only Host type that touches module implementations, and only
/// through their registration entry points (brief §5 rule 4).
/// </summary>
internal static class EngineComposition
{
    public static WebApplicationBuilder AddEngine(this WebApplicationBuilder builder)
    {
        EnginePaths.ApplyDefaultDatabasePath(builder.Configuration, builder.Environment.ContentRootPath);
        EnginePaths.ApplyDefaultConsensusDirectory(builder.Configuration, builder.Environment.ContentRootPath);

        // Serve wwwroot from the project when running from build output in any environment.
        builder.WebHost.UseStaticWebAssets();
        var services = builder.Services;
        services.AddEngineCore(builder.Configuration);
        services.AddApiSecurity(builder.Configuration);
        services.AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new UtcDateTimeOffsetModelBinderProvider());
                options.Filters.Add<RejectUnreadableParametersFilter>();
            })
            .AddJsonOptions(options =>
            {
                ApiJson.Configure(options.JsonSerializerOptions);
                ContractJson.RequireContractValueTypes(options.JsonSerializerOptions);
            });
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddScoped<EngineHealthService>();
        services.AddHostedService<LedgerVerificationScheduler>();
        return builder;
    }

    /// <summary>Everything the engine needs without the web surface; shared by the CLI.</summary>
    public static IServiceCollection AddEngineCore(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddConfigurationModule(configuration)
            .AddIngestionModule(configuration)
            .AddClassificationModule(configuration)
            .AddDecisionModule(configuration)
            .AddAnalyticsModule(configuration);

    /// <summary>Fails fast on an unreadable registry, then migrates the database.</summary>
    public static Task InitializeEngineAsync(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<IndicatorRegistry>();
        return EngineDatabaseInitializer.InitializeAsync(app.Services, app.Lifetime.ApplicationStopping);
    }

    public static WebApplication UseEngine(this WebApplication app)
    {
        app.UseApiExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapControllers();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }
}
