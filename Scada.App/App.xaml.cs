using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Drivers;
using Scada.Drivers.Simulator;
using Scada.Infrastructure.Configuration;
using Scada.Infrastructure.Persistence;
using Scada.Runtime.Drivers;
using Scada.Runtime.Engine;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;

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
            ConfigurationValidator.Validate(startupOptions);

            builder.Logging.AddDebug();
            builder.Services.AddSingleton(startupOptions);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<SimulatorValueGenerator>();
            builder.Services.AddSingleton<SimulatorPlcDriver>();
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
            builder.Services.AddHostedService<PollingRuntimeService>();
            builder.Services.AddSingleton<ProjectEditSession>(_ => new ProjectEditSession(
                startupOptions,
                projectPath,
                projectStore));
            builder.Services.AddSingleton<IClipboardAdapter, WpfClipboardAdapter>();
            builder.Services.AddSingleton<ITagImportDecisionService, WpfTagImportDecisionService>();
            builder.Services.AddSingleton<IDeleteConfirmation, WpfDeleteConfirmation>();
            builder.Services.AddSingleton<OperationViewModel>();
            builder.Services.AddSingleton<MachineSettingsViewModel>();
            builder.Services.AddSingleton<MonitoringViewModel>();
            builder.Services.AddSingleton<TagManagerViewModel>();
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
