using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrimeScore.Ledger;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Configuration.Features;
using PrimeScore.Modules.Configuration.Settings;
using PrimeScore.Modules.Configuration.Storage;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Configuration;

/// <summary>Registration entry point: the only public type of this module (brief §5 rule 4).</summary>
public static class ConfigurationModule
{
    public static IServiceCollection AddConfigurationModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<ConfigurationReadStore>();
        services.AddSingleton<SettingsWriter>();
        services.AddSingleton<IEngineSchema, ConfigurationSchema>();
        services.AddSingleton<ILedgerProjection, SettingsProjection>();

        services.AddScoped<IQueryHandler<GetActiveSettings, SettingsVersion>, GetActiveSettingsHandler>();
        services.AddScoped<IQueryHandler<GetSettingsVersion, SettingsVersion?>, GetSettingsVersionHandler>();
        services.AddScoped<IQueryHandler<GetSettingsHistory, IReadOnlyList<SettingsVersion>>, GetSettingsHistoryHandler>();
        services.AddScoped<ICommandHandler<SetWeightingScheme, SettingsChangeAck>, SetWeightingSchemeHandler>();
        services.AddScoped<ICommandHandler<SetDislocationSettings, SettingsChangeAck>, SetDislocationSettingsHandler>();
        services.AddScoped<ICommandHandler<ReplaceSettings, SettingsChangeAck>, ReplaceSettingsHandler>();
        return services;
    }
}
