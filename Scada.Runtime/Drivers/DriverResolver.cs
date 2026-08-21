using Scada.Core.Devices;
using Scada.Core.Drivers;

namespace Scada.Runtime.Drivers;

public sealed class DriverResolver : IPlcDriverResolver
{
    private readonly IReadOnlyDictionary<string, DriverRegistration> _registrations;

    public DriverResolver(IEnumerable<DriverRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var map = new Dictionary<string, DriverRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (!map.TryAdd(registration.DriverType, registration))
            {
                throw new InvalidOperationException($"Driver type '{registration.DriverType}' is registered more than once.");
            }
        }

        _registrations = map;
    }

    public IPlcDriverLease Acquire(DeviceDefinition device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_registrations.TryGetValue(device.DriverType, out var registration))
        {
            throw new InvalidOperationException(
                $"No PLC driver is registered for device '{device.Id}' with driver type '{device.DriverType}'.");
        }

        var driver = registration.Factory(device)
            ?? throw new InvalidOperationException($"Driver factory returned null for device '{device.Id}'.");

        if (!string.Equals(driver.DriverType, registration.DriverType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Driver registration '{registration.DriverType}' created driver type '{driver.DriverType}'.");
        }

        return new DriverLease(driver, registration.OwnsCreatedDriver);
    }

    private sealed class DriverLease(IPlcDriver driver, bool ownsDriver) : IPlcDriverLease
    {
        private int _disposed;

        public IPlcDriver Driver { get; } = driver;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || !ownsDriver)
            {
                return;
            }

            switch (Driver)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }
}
