using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Drivers;
using Scada.Core.History;
using Scada.Drivers.Simulator;
using Scada.Infrastructure.Configuration;
using Scada.Infrastructure.History;
using Scada.Infrastructure.History.Influx;
using Scada.Infrastructure.Persistence;
using Scada.Infrastructure.Mqtt;
using Scada.Runtime.Drivers;
using Scada.Runtime.Engine;
using Scada.Runtime.Historian;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Health;
using Scada.Core.Mqtt;
using Scada.Core.Alarms;
using Scada.Infrastructure.Alarms;
using Scada.Runtime.Alarms;

namespace Scada.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var settings = new HostApplicationBuilderSettings
            {
                Args = e.Args,
                ContentRootPath = AppContext.BaseDirectory
            };
            var builder = Host.CreateApplicationBuilder(settings);
            builder.Configuration.SetBasePath(AppContext.BaseDirectory);
            builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

            var projectPath = ResolveProjectPath(e.Args);
            var projectStore = projectPath is null ? null : new ProjectConfigurationStore(projectPath);
            var projectDocument = projectStore?.Load();
            var startupOptions = projectDocument?.Scada
                ?? ConfigurationRegistration.CreateOptions(builder.Configuration);
            var simulatorEngineeringProvider = new SimulatorEngineeringProvider();
            ConfigurationValidator.Validate(startupOptions, [simulatorEngineeringProvider]);

            builder.Logging.AddDebug();
            builder.Services.AddSingleton(startupOptions);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<SimulatorValueGenerator>();
            builder.Services.AddSingleton<SimulatorPlcDriver>();
            builder.Services.AddSingleton<Scada.Core.Drivers.IDriverEngineeringProvider>(simulatorEngineeringProvider);
            builder.Services.AddSingleton<IPlcDriverResolver>(services => new DriverResolver(
            [
                DriverRegistration.Shared(
                    "Simulator",
                    services.GetRequiredService<SimulatorPlcDriver>())
            ]));
            builder.Services.AddSingleton<TagCache>();
            builder.Services.AddSingleton<ITagCache>(services => services.GetRequiredService<TagCache>());
            builder.Services.AddSingleton<TagEngine>();
            builder.Services.AddSingleton<ScadaRuntime>();
            builder.Services.AddSingleton<DeviceManager>();
            if (startupOptions.Historian.StorageProvider == HistoryStorageProvider.InfluxDb2)
            {
                builder.Services.AddSingleton<BufferedInfluxHistoryStore>(services =>
                    new BufferedInfluxHistoryStore(
                        projectPath,
                        startupOptions.Historian,
                        services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BufferedInfluxHistoryStore>>(),
                        services.GetRequiredService<TimeProvider>()));
                builder.Services.AddSingleton<IHistoryStore>(services =>
                    services.GetRequiredService<BufferedInfluxHistoryStore>());
                builder.Services.AddSingleton<IHistoryStoreDiagnostics>(services =>
                    services.GetRequiredService<BufferedInfluxHistoryStore>());
                builder.Services.AddSingleton<IHistoryStoreMaintenance>(services =>
                    services.GetRequiredService<BufferedInfluxHistoryStore>());
            }
            else
            {
                builder.Services.AddSingleton<IHistoryStore>(_ => new SqliteHistoryStore(
                    projectPath,
                    startupOptions.Historian.DatabasePath));
            }
            builder.Services.AddSingleton<HistorianRuntimeService>();
            builder.Services.AddSingleton<IHostedService>(services =>
                services.GetRequiredService<HistorianRuntimeService>());
            builder.Services.AddSingleton<IMqttTransport, MqttNetTransport>();
            builder.Services.AddSingleton<MqttRuntimeService>();
            builder.Services.AddSingleton<IHostedService>(services =>
                services.GetRequiredService<MqttRuntimeService>());
            builder.Services.AddSingleton<IAlarmEventStore>(_ => new SqliteAlarmEventStore(
                projectPath,
                startupOptions.Alarms.DatabasePath));
            builder.Services.AddSingleton<AlarmRuntimeService>();
            builder.Services.AddSingleton<IHostedService>(services =>
                services.GetRequiredService<AlarmRuntimeService>());
            builder.Services.AddSingleton<PollingRuntimeService>();
            builder.Services.AddSingleton<IHostedService>(services =>
                services.GetRequiredService<PollingRuntimeService>());
            builder.Services.AddSingleton<IRuntimeHealthDispatcher, WpfRuntimeHealthDispatcher>();
            builder.Services.AddSingleton<IMonitoringDispatcher, WpfMonitoringDispatcher>();
            builder.Services.AddSingleton<RuntimeHealthService>();
            builder.Services.AddSingleton<IHostedService>(services =>
                services.GetRequiredService<RuntimeHealthService>());
            builder.Services.AddSingleton<RuntimeHealthPresentationService>();
            builder.Services.AddSingleton<ProjectEditSession>(services => new ProjectEditSession(
                startupOptions,
                projectPath,
                projectStore,
                services.GetServices<Scada.Core.Drivers.IDriverEngineeringProvider>()));
            builder.Services.AddSingleton<IClipboardAdapter, WpfClipboardAdapter>();
            builder.Services.AddSingleton<ITagImportDecisionService, WpfTagImportDecisionService>();
            builder.Services.AddSingleton<IDeleteConfirmation, WpfDeleteConfirmation>();
            builder.Services.AddSingleton<IHistoryBufferConfirmation, WpfHistoryBufferConfirmation>();
            builder.Services.AddSingleton<IHistoryConnectionTester, InfluxHistoryConnectionTester>();
            builder.Services.AddSingleton<IHistoryRetentionManager, InfluxHistoryRetentionManager>();
            builder.Services.AddSingleton<IMqttConnectionTester, MqttConnectionTester>();
            builder.Services.AddSingleton<OperationViewModel>();
            builder.Services.AddSingleton<MachineSettingsViewModel>();
            builder.Services.AddSingleton<MonitoringViewModel>();
            builder.Services.AddSingleton<TagManagerViewModel>();
            builder.Services.AddSingleton<HistorySettingsViewModel>();
            builder.Services.AddSingleton<MqttSettingsViewModel>();
            builder.Services.AddSingleton<AlarmMonitoringViewModel>();
            builder.Services.AddSingleton<AlarmEngineeringViewModel>();
            builder.Services.AddSingleton<SystemServicesViewModel>();
            builder.Services.AddSingleton<EngineeringDiagnosticsViewModel>();
            builder.Services.AddSingleton<EngineeringDevicesViewModel>();
            builder.Services.AddSingleton<EngineeringViewModel>();
            builder.Services.AddSingleton<NavigationService>();
            builder.Services.AddSingleton<ShellViewModel>();
            builder.Services.AddTransient<MainWindow>();

            _host = builder.Build();
            await _host.StartAsync();
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "SCADA startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _host?.Dispose();
            _host = null;
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static ProjectPath? ResolveProjectPath(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--project-file", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new InvalidOperationException("--project-file requires an absolute project.json path.");
                }

                return new ProjectPathResolver().Resolve(args[++index]);
            }

            const string prefix = "--project-file=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new ProjectPathResolver().Resolve(argument[prefix.Length..]);
            }
        }

        return null;
    }
}
