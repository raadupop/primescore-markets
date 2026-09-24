using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PrimeScore.Modules.Ingestion;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class IngestionModule
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }
}
