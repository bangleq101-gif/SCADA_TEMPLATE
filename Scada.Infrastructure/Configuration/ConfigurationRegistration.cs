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
        var options = configuration.GetSection("Scada").Get<RuntimeOptions>() ?? new RuntimeOptions();
        ConfigurationValidator.Validate(options);
        services.AddSingleton(options);
        return services;
    }
}
