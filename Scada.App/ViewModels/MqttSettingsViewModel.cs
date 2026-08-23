using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Scada.App.Services;
using Scada.Core.Mqtt;
using Scada.Runtime.Mqtt;

namespace Scada.App.ViewModels;

public sealed class MqttSettingsViewModel : INotifyPropertyChanged, IWorkspaceLifecycle
{
    private readonly ProjectEditSession _session;
    private readonly MqttRuntimeService _runtime;
    private readonly IMqttConnectionTester? _tester;
    public MqttSettingsViewModel(ProjectEditSession session, MqttRuntimeService runtime, IMqttConnectionTester? tester = null)
    { _session = session; _runtime = runtime; _tester = tester; SaveCommand = new RelayCommand(_ => _session.TrySave()); RevertCommand = new RelayCommand(_ => _session.Revert()); TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public AsyncRelayCommand TestConnectionCommand { get; }
    public string TestConnectionStatus { get; private set; } = "Not tested";
    public bool Enabled { get => _session.WorkingProject.Mqtt.Enabled; set => Set(value, x => _session.WorkingProject.Mqtt.Enabled = x); }
    public string Host { get => _session.WorkingProject.Mqtt.Host; set => Set(value, x => _session.WorkingProject.Mqtt.Host = x); }
    public int Port { get => _session.WorkingProject.Mqtt.Port; set => Set(value, x => _session.WorkingProject.Mqtt.Port = x); }
    public string BaseTopic { get => _session.WorkingProject.Mqtt.BaseTopic; set => Set(value, x => _session.WorkingProject.Mqtt.BaseTopic = x); }
    public string TopicTemplate { get => _session.WorkingProject.Mqtt.TopicTemplate; set => Set(value, x => _session.WorkingProject.Mqtt.TopicTemplate = x); }
    public string Username { get => _session.WorkingProject.Mqtt.Username; set => Set(value, x => _session.WorkingProject.Mqtt.Username = x); }
    public string PasswordReference { get => _session.WorkingProject.Mqtt.PasswordReference; set => Set(value, x => _session.WorkingProject.Mqtt.PasswordReference = x); }
    public bool UseTls { get => _session.WorkingProject.Mqtt.UseTls; set => Set(value, x => _session.WorkingProject.Mqtt.UseTls = x); }
    public MqttRuntimeSnapshot RuntimeStatus => _runtime.Snapshot;
    public void Activate() => OnPropertyChanged(nameof(RuntimeStatus));
    public void Deactivate() { }
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    { if (_tester is null) { TestConnectionStatus = "Test connection is unavailable."; OnPropertyChanged(nameof(TestConnectionStatus)); return; }
      try { var result = await _tester.TestAsync(_session.WorkingProject.Mqtt, _session.StartupProject.RuntimeId, cancellationToken); TestConnectionStatus = result.IsAccepted ? "Connection accepted." : result.ErrorMessage ?? result.ErrorCode ?? "Connection rejected."; }
      catch (Exception exception) { TestConnectionStatus = exception.Message; } OnPropertyChanged(nameof(TestConnectionStatus)); }
    private void Set<T>(T value, Action<T> apply, [CallerMemberName] string? property = null) { apply(value); _session.MarkChanged(); OnPropertyChanged(property); }
    private void OnPropertyChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
