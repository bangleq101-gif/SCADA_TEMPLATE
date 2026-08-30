using Scada.Core.Configuration;
using Scada.Core.Drivers;

namespace Scada.Infrastructure.Configuration;

public static class ConfigurationValidator
{
    public static IReadOnlyList<ValidationIssue> CollectIssues(
        RuntimeOptions options,
        IEnumerable<IDriverEngineeringProvider>? driverProviders = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issues = RuntimeOptionsValidation.CollectIssues(options).ToList();
        var providerArray = (driverProviders ?? []).ToArray();
        if (providerArray.Length == 0)
        {
            return issues;
        }

        var providers = providerArray
            .GroupBy(provider => provider.DriverType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var device in options.Devices ?? [])
        {
            if (providers.TryGetValue(device.DriverType, out var provider))
            {
                issues.AddRange(provider.Validate(device));
            }
            else if (device.Enabled && !string.IsNullOrWhiteSpace(device.DriverType))
            {
                issues.Add(new ValidationIssue(
                    "DEVICE_DRIVER_UNSUPPORTED",
                    ValidationSeverity.Error,
                    "Device",
                    device.Id,
                    nameof(device.DriverType),
                    $"No engineering provider is registered for driver type '{device.DriverType}'."));
            }
        }

        var devices = (options.Devices ?? [])
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var tag in options.Tags ?? [])
        {
            if (!devices.TryGetValue(tag.DeviceId, out var device) ||
                !providers.TryGetValue(device.DriverType, out var provider) ||
                provider is not IDriverTagEngineeringProvider tagProvider)
            {
                continue;
            }

            issues.AddRange(tagProvider.ValidateTag(device, tag));
        }

        return issues;
    }

    public static void Validate(
        RuntimeOptions options,
        IEnumerable<IDriverEngineeringProvider>? driverProviders = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issues = CollectIssues(options, driverProviders);
        var blockingIssues = issues.Where(issue => issue.IsBlocking).ToArray();
        if (blockingIssues.Length > 0)
        {
            throw new ConfigurationValidationException(blockingIssues);
        }
    }
}
