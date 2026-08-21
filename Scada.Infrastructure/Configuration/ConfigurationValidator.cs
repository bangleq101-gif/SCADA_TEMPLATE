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

        ValidatePolling(options);

        var scanGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scanGroup in options.ScanGroups)
        {
            if (string.IsNullOrWhiteSpace(scanGroup.Name) || !scanGroups.Add(scanGroup.Name))
            {
                throw new InvalidOperationException($"Scan group '{scanGroup.Name}' is missing or duplicated.");
            }

            if (scanGroup.IntervalMilliseconds <= 0)
            {
                throw new InvalidOperationException($"Scan group '{scanGroup.Name}' interval must be greater than zero.");
            }
        }

        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in options.Devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id) || !deviceIds.Add(device.Id))
            {
                throw new InvalidOperationException($"Device id '{device.Id}' is missing or duplicated.");
            }

            if (device.Enabled && string.IsNullOrWhiteSpace(device.DriverType))
            {
                throw new InvalidOperationException($"Enabled device '{device.Id}' requires a driver type.");
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

            if (tag.Enabled && !scanGroups.Contains(tag.ScanGroup))
            {
                throw new InvalidOperationException($"Tag '{tag.Id}' references missing scan group '{tag.ScanGroup}'.");
            }
        }
    }

    private static void ValidatePolling(RuntimeOptions options)
    {
        if (options.Polling.ConnectTimeoutMilliseconds <= 0 ||
            options.Polling.ReadTimeoutMilliseconds <= 0 ||
            options.Polling.DisconnectTimeoutMilliseconds <= 0 ||
            options.Polling.InitialReconnectDelayMilliseconds <= 0 ||
            options.Polling.MaxReconnectDelayMilliseconds <= 0 ||
            options.Polling.InitialReconnectDelayMilliseconds > options.Polling.MaxReconnectDelayMilliseconds ||
            options.Polling.ShutdownTimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException("Scada.Polling timeout, reconnect and shutdown settings are invalid.");
        }
    }
}
