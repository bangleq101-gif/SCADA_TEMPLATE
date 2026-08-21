using Scada.Core.Configuration;

namespace Scada.Infrastructure.Configuration;

public static class ConfigurationValidator
{
    public static void Validate(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RuntimeId))
        {
            throw new InvalidOperationException("Scada.RuntimeId is required.");
        }

        if (options.PollingIntervalMilliseconds <= 0)
        {
            throw new InvalidOperationException("Scada.PollingIntervalMilliseconds must be greater than zero.");
        }

        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in options.Devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id) || !deviceIds.Add(device.Id))
            {
                throw new InvalidOperationException($"Device id '{device.Id}' is missing or duplicated.");
            }
        }

        var tagIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in options.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Id) || !tagIds.Add(tag.Id))
            {
                throw new InvalidOperationException($"Tag id '{tag.Id}' is missing or duplicated.");
            }

            if (!deviceIds.Contains(tag.DeviceId))
            {
                throw new InvalidOperationException($"Tag '{tag.Id}' references missing device '{tag.DeviceId}'.");
            }

            if (string.IsNullOrWhiteSpace(tag.Address))
            {
                throw new InvalidOperationException($"Tag '{tag.Id}' requires an address.");
            }
        }
    }
}
