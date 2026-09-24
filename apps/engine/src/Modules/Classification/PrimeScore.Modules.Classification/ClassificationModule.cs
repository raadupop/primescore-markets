using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Classification.Classifier;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Classification;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class ClassificationModule
{
    public static IServiceCollection AddClassificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<ClassifierOptions>(configuration.GetSection(ClassifierOptions.Section));
        services.AddHttpClient(ClassifierOptions.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ClassifierOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddScoped<IQueryHandler<GetClassifierHealth, ClassifierHealth>, GetClassifierHealthHandler>();
        return services;
    }
}
