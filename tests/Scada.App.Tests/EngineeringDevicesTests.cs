using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Drivers.Simulator;
using Xunit;

namespace Scada.App.Tests;

public sealed class EngineeringDevicesTests
{
    [Fact]
    public async Task DeviceWorkspaceEditsAreOwnedByProjectSessionAndBrowsesAddresses()
    {
        var provider = new SimulatorEngineeringProvider();
        var options = new RuntimeOptions
        {
            Devices =
            [
                new DeviceDefinition
                {
                    Id = "SIM01",
                    Name = "Simulator",
                    DriverType = "Simulator"
                }
            ]
        };
        var session = new ProjectEditSession(options, null, null, [provider]);
        using var viewModel = new EngineeringDevicesViewModel(session, [provider]);

        var device = Assert.Single(viewModel.Devices);
        viewModel.SelectedDevice = device;
        device.Name = "Updated Simulator";
        await viewModel.BrowseAddressesCommand.RunAsync();

        Assert.True(session.IsDirty);
        Assert.Equal("Simulator", device.DriverType);
        Assert.Equal(["A1", "B1", "C1", "S1"], viewModel.AddressCandidates.Select(candidate => candidate.Address));
        Assert.Contains("Updated Simulator", session.WorkingProject.Devices[0].Name);
        Assert.Equal("Simulator", session.SavedProject.Devices[0].Name);
    }

    [Fact]
    public void InvalidSimulatorOptionIsBlockingAndDiscoverable()
    {
        var provider = new SimulatorEngineeringProvider();
        var options = new RuntimeOptions
        {
            Devices =
            [
                new DeviceDefinition
                {
                    Id = "SIM01",
                    DriverType = "Simulator",
                    ConnectionOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [SimulatorFaultOptions.FaultModeKey] = "not-valid"
                    }
                }
            ]
        };

        var session = new ProjectEditSession(options, null, null, new IDriverEngineeringProvider[] { provider });

        Assert.True(session.HasBlockingIssues);
        Assert.Contains(session.ValidationIssues, issue =>
            issue.Code == "SIMULATOR_OPTION_INVALID" &&
            issue.ObjectId == "SIM01");
    }

    [Fact]
    public void RevertDiscardsDeviceDraftThroughTheSingleSessionAuthority()
    {
        var provider = new SimulatorEngineeringProvider();
        var options = new RuntimeOptions
        {
            Devices = [new DeviceDefinition { Id = "SIM01", Name = "Original", DriverType = "Simulator" }]
        };
        var session = new ProjectEditSession(options, null, null, [provider]);
        using var viewModel = new EngineeringDevicesViewModel(session, [provider]);

        viewModel.SelectedDevice!.Name = "Draft";
        viewModel.RevertCommand.Execute(null);

        Assert.Equal("Original", Assert.Single(viewModel.Devices).Name);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void DeviceWorkspaceUsesOnlyTheEngineeringRoute()
    {
        var provider = new SimulatorEngineeringProvider();
        var session = new ProjectEditSession(new RuntimeOptions(), null, null, [provider]);
        using var devices = new EngineeringDevicesViewModel(session, [provider]);
        var navigation = new NavigationService(
            new OperationViewModel(new RuntimeOptions()),
            new MachineSettingsViewModel(),
            new MonitoringViewModel(new TestTagCache(), new RuntimeOptions()),
            new EngineeringViewModel(),
            engineeringDevices: devices);

        Assert.True(navigation.HasRoute(NavigationService.EngineeringDevicesRoute));
        Assert.False(navigation.HasRoute("engineering.device-SIM01"));
    }

    [Fact]
    public async Task AddressBrowseResultIsIgnoredAfterTheSelectedDeviceChanges()
    {
        var provider = new DeferredEngineeringProvider();
        var options = new RuntimeOptions
        {
            Devices =
            [
                new DeviceDefinition { Id = "DEVICE_A", DriverType = provider.DriverType },
                new DeviceDefinition { Id = "DEVICE_B", DriverType = provider.DriverType }
            ]
        };
        var session = new ProjectEditSession(options, null, null, [provider]);
        using var viewModel = new EngineeringDevicesViewModel(session, [provider]);

        viewModel.SelectedDevice = viewModel.Devices[0];
        var browse = viewModel.BrowseAddressesCommand.RunAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        viewModel.SelectedDevice = viewModel.Devices[1];
        provider.Complete([new AddressBrowseCandidate("STALE_A1", Scada.Core.Tags.TagDataType.Double, "stale")]);
        await browse;

        Assert.Empty(viewModel.AddressCandidates);
        Assert.DoesNotContain("Loaded", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DeferredEngineeringProvider : IDriverEngineeringProvider
    {
        private readonly TaskCompletionSource<IReadOnlyList<AddressBrowseCandidate>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DriverType => "Deferred";
        public IReadOnlyList<DriverOptionDefinition> OptionDefinitions => [];
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<ValidationIssue> Validate(DeviceDefinition device) => [];

        public Task<IReadOnlyList<AddressBrowseCandidate>> BrowseAddressesAsync(
            DeviceDefinition device,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            return _completion.Task;
        }

        public void Complete(IReadOnlyList<AddressBrowseCandidate> candidates) => _completion.TrySetResult(candidates);
    }
}
