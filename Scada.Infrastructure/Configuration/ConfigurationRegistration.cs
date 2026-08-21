using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scada.Core.Configuration;

namespace Scada.Infrastructure.Configuration;

public static class ConfigurationRegistration
{
    public static IServiceCollection AddScadaConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new RuntimeOptions();
        var scadaSection = configuration.GetSection("Scada");
        if (scadaSection.GetSection("ScanGroups").Exists())
        {
            options.ScanGroups.Clear();
        }

        scadaSection.Bind(options);
        ConfigurationValidator.Validate(options);
        services.AddSingleton(options);
        return services;
    }
}
