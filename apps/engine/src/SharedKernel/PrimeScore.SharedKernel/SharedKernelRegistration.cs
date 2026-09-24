using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PrimeScore.SharedKernel.Messaging;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.SharedKernel;

public static class SharedKernelRegistration
{
    /// <summary>Clock, in-process integration events and the indicator registry (<c>Registry:Path</c>).</summary>
    public static IServiceCollection AddSharedKernel(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<IIntegrationEventPublisher, InProcessIntegrationEventPublisher>();
        services.TryAddSingleton(provider =>
        {
            var baseDirectory = provider.GetService<IHostEnvironment>()?.ContentRootPath ?? AppContext.BaseDirectory;
            var path = IndicatorRegistryLoader.ResolvePath(configuration["Registry:Path"], baseDirectory);
            return IndicatorRegistryLoader.Load(path);
        });
        return services;
    }
}
