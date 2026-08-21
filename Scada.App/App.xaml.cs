using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.App.ViewModels;
using Scada.Core.Drivers;
using Scada.Drivers.Simulator;
using Scada.Infrastructure.Configuration;
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

        var settings = new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory
        };
        var builder = Host.CreateApplicationBuilder(settings);
        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        builder.Logging.AddDebug();
        builder.Services.AddScadaConfiguration(builder.Configuration);
        builder.Services.AddSingleton<SimulatorValueGenerator>();
        builder.Services.AddSingleton<IPlcDriver, SimulatorPlcDriver>();
        builder.Services.AddSingleton<TagCache>();
        builder.Services.AddSingleton<ITagCache>(services => services.GetRequiredService<TagCache>());
        builder.Services.AddSingleton<TagEngine>();
        builder.Services.AddSingleton<ScadaRuntime>();
        builder.Services.AddHostedService<PollingRuntimeService>();
        builder.Services.AddSingleton<NavigationService>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<MainWindow>();

        _host = builder.Build();
        await _host.StartAsync();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
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
}
