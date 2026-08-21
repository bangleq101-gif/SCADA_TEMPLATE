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
        var options = new RuntimeOptions
        {
            // RuntimeOptions has programmatic defaults. Clear collection defaults before binding
            // so configuration entries do not get appended to those defaults as duplicates.
            ScanGroups = []
        };
        configuration.GetSection("Scada").Bind(options);
        ConfigurationValidator.Validate(options);
        services.AddSingleton(options);
        return services;
    }
}
