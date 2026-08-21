using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Runtime.Drivers;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class DriverResolverTests
{
    [Fact]
    public async Task SelectsDriverByDeviceDriverType()
    {
        var simulator = new TestDriver("Simulator");
        var resolver = new DriverResolver([DriverRegistration.Shared("Simulator", simulator)]);

        await using var lease = resolver.Acquire(new DeviceDefinition { Id = "SIM01", DriverType = "Simulator" });

        Assert.Same(simulator, lease.Driver);
    }

    [Fact]
    public void UnknownDriverTypeIsRejected()
    {
        var resolver = new DriverResolver([]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            resolver.Acquire(new DeviceDefinition { Id = "PLC01", DriverType = "Siemens" }));

        Assert.Contains("Siemens", exception.Message);
    }

    [Fact]
    public void DuplicateDriverRegistrationsAreRejected()
    {
        var first = new TestDriver("Simulator");
        var second = new TestDriver("Simulator");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DriverResolver(
            [
                DriverRegistration.Shared("Simulator", first),
                DriverRegistration.Shared("simulator", second)
            ]));

        Assert.Contains("registered more than once", exception.Message);
    }

    [Fact]
    public async Task SharedLeaseDoesNotDisposeSharedDriver()
    {
        var driver = new TestDriver("Simulator");
        var resolver = new DriverResolver([DriverRegistration.Shared("Simulator", driver)]);
        var lease = resolver.Acquire(new DeviceDefinition { Id = "SIM01", DriverType = "Simulator" });

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Equal(0, driver.DisposeCount);
    }

    [Fact]
    public async Task PerDeviceRegistrationCreatesAndDisposesDistinctInstances()
    {
        var created = new List<TestDriver>();
        var resolver = new DriverResolver(
        [
            DriverRegistration.PerDevice("Siemens", device =>
            {
                var driver = new TestDriver("Siemens");
                created.Add(driver);
                return driver;
            })
        ]);

        var firstLease = resolver.Acquire(new DeviceDefinition { Id = "S7_01", DriverType = "Siemens" });
        var secondLease = resolver.Acquire(new DeviceDefinition { Id = "S7_02", DriverType = "Siemens" });

        Assert.Equal(2, created.Count);
        Assert.NotSame(firstLease.Driver, secondLease.Driver);

        await firstLease.DisposeAsync();
        await firstLease.DisposeAsync();
        await secondLease.DisposeAsync();

        Assert.Equal(1, created[0].DisposeCount);
        Assert.Equal(1, created[1].DisposeCount);
    }

    private sealed class TestDriver(string driverType) : IPlcDriver, IDisposable
    {
        public string DriverType { get; } = driverType;
        public int DisposeCount { get; private set; }

        public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DriverReadResult>>([]);

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose() => DisposeCount++;
    }
}
