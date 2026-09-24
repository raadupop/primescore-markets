using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PrimeScore.Modules.Decision;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class DecisionModule
{
    public static IServiceCollection AddDecisionModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }
}
