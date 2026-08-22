using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scada.Core.Configuration;
using Scada.Core.History;

namespace Scada.Infrastructure.Configuration;

public static class ConfigurationRegistration
{
    public static RuntimeOptions CreateOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RuntimeOptions();
        var scadaSection = configuration.GetSection("Scada");
        if (scadaSection.GetSection("ScanGroups").Exists())
        {
            options.ScanGroups.Clear();
        }

        var historianSection = scadaSection.GetSection(nameof(RuntimeOptions.Historian));
        var profileSection = historianSection.GetSection(nameof(HistorianOptions.Profiles));
        if (profileSection.Exists() || profileSection.Value is not null)
        {
            options.Historian.Profiles.Clear();
        }

        scadaSection.Bind(options);
        return options;
    }

    public static IServiceCollection AddScadaConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = CreateOptions(configuration);
        ConfigurationValidator.Validate(options);
        services.AddSingleton(options);
        return services;
    }
}
