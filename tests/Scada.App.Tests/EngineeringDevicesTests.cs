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
}
