using Scada.Core.Devices;
using Scada.Core.Drivers;

namespace Scada.Runtime.Drivers;

public sealed class DriverRegistration
{
    private DriverRegistration(
        string driverType,
        Func<DeviceDefinition, IPlcDriver> factory,
        bool ownsCreatedDriver)
    {
        if (string.IsNullOrWhiteSpace(driverType))
        {
            throw new ArgumentException("Driver type is required.", nameof(driverType));
        }

        DriverType = driverType.Trim();
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        OwnsCreatedDriver = ownsCreatedDriver;
    }

    public string DriverType { get; }
    internal Func<DeviceDefinition, IPlcDriver> Factory { get; }
    internal bool OwnsCreatedDriver { get; }

    public static DriverRegistration Shared(string driverType, IPlcDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        return new DriverRegistration(driverType, _ => driver, ownsCreatedDriver: false);
    }

    public static DriverRegistration PerDevice(
        string driverType,
        Func<DeviceDefinition, IPlcDriver> factory)
    {
        return new DriverRegistration(driverType, factory, ownsCreatedDriver: true);
    }
}
