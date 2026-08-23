using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.Core.Configuration;
using Scada.Core.Mqtt;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Mqtt;

public sealed class MqttRuntimeService : IHostedService, IAsyncDisposable
{
    private readonly RuntimeOptions _options;
    private readonly ITagCache _tagCache;
    private readonly IMqttTransport _transport;
    private readonly ILogger<MqttRuntimeService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly MqttProfileEvaluator _evaluator;
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nextPeriodic = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private MqttRuntimeState _state = MqttRuntimeState.Disabled;
    private long _published, _coalesced, _rejected, _failures, _reconnects;
    private DateTimeOffset? _lastConnected, _lastPublished;
    private string? _errorCode, _errorMessage;

    public MqttRuntimeService(RuntimeOptions options, ITagCache tagCache, IMqttTransport transport, ILogger<MqttRuntimeService> logger, TimeProvider timeProvider)
    { _options = options; _tagCache = tagCache; _transport = transport; _logger = logger; _timeProvider = timeProvider; _evaluator = new MqttProfileEvaluator(timeProvider); }

    public MqttRuntimeSnapshot Snapshot => new(_state, _subscriptions.Count, _pending.Count, Interlocked.Read(ref _published), Interlocked.Read(ref _coalesced), Interlocked.Read(ref _rejected), Interlocked.Read(ref _failures), Interlocked.Read(ref _reconnects), _lastConnected, _lastPublished, _errorCode, _errorMessage);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Mqtt.Enabled) { _state = MqttRuntimeState.Disabled; return Task.CompletedTask; }
        if (!TryGetPassword(out var password)) { SetState(MqttRuntimeState.ConfigurationRequired, "MQTT_PASSWORD_REFERENCE", "MQTT password reference cannot be resolved."); return Task.CompletedTask; }
        var profiles = new MqttProfileRegistry(_options.Mqtt.Profiles);
        foreach (var tag in _options.Tags.Where(tag => tag.Enabled && tag.MqttPublishEnabled && profiles.TryGet(tag.MqttProfile, out _)))
        {
            var subscription = _tagCache.Subscribe(tag.Id, value => Queue(tag, value));
            _subscriptions.Add(subscription);
            if (_tagCache.TryGet(tag.Id, out var value) && value is not null) Queue(tag, value, force: true);
        }
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _state = MqttRuntimeState.Starting;
        _worker = Task.Run(() => RunAsync(password, _lifetime.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _state = MqttRuntimeState.Stopping;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear(); _lifetime?.Cancel();
        if (_worker is not null) { using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); budget.CancelAfter(_options.Mqtt.ShutdownTimeoutMilliseconds); try { await _worker.WaitAsync(budget.Token).ConfigureAwait(false); } catch (OperationCanceledException) { } }
        try { await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false); } catch (Exception ex) { _logger.LogWarning(ex, "MQTT disconnect failed."); }
        _state = MqttRuntimeState.Disabled; _worker = null; _lifetime?.Dispose(); _lifetime = null;
    }
    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private void Queue(TagDefinition tag, TagValue value, bool force = false)
    {
        if (!new MqttProfileRegistry(_options.Mqtt.Profiles).TryGet(tag.MqttProfile, out var profile) || profile is null || (!force && !_evaluator.ShouldPublish(tag, profile, value))) return;
        if (!MqttTopicBuilder.TryBuild(_options.RuntimeId, tag, _options.Mqtt, out var topic)) { Interlocked.Increment(ref _rejected); return; }
        var pending = new Pending(tag, value, topic!);
        if (_pending.TryGetValue(tag.Id, out _)) Interlocked.Increment(ref _coalesced);
        _pending[tag.Id] = pending; _signal.Writer.TryWrite(0);
    }

    private async Task RunAsync(string? password, CancellationToken cancellationToken)
    {
        var delay = _options.Mqtt.ReconnectInitialDelayMilliseconds;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_transport.IsConnected)
            {
                SetState(MqttRuntimeState.Connecting, null, null); Interlocked.Increment(ref _reconnects);
                try
                {
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); connectCts.CancelAfter(_options.Mqtt.ConnectionTimeoutMilliseconds);
                    var result = await _transport.ConnectAsync(new MqttConnectRequest(_options.Mqtt.Host, _options.Mqtt.Port, _options.Mqtt.ProtocolVersion, ResolveClientId(), _options.Mqtt.Username, password, _options.Mqtt.UseTls, _options.Mqtt.KeepAliveSeconds, TimeSpan.FromMilliseconds(_options.Mqtt.ConnectionTimeoutMilliseconds)), connectCts.Token).ConfigureAwait(false);
                    if (!result.IsAccepted) throw new InvalidOperationException(result.ErrorMessage ?? result.ErrorCode ?? "Broker rejected connection.");
                    _lastConnected = _timeProvider.GetUtcNow(); SetState(MqttRuntimeState.Online, null, null); delay = _options.Mqtt.ReconnectInitialDelayMilliseconds;
                    foreach (var tag in _options.Tags.Where(tag => tag.Enabled && tag.MqttPublishEnabled)) if (_tagCache.TryGet(tag.Id, out var cached) && cached is not null) Queue(tag, cached, force: true);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                { Interlocked.Increment(ref _failures); SetState(MqttRuntimeState.Offline, "MQTT_CONNECT", ex.Message); await Task.Delay(TimeSpan.FromMilliseconds(delay), _timeProvider, cancellationToken).ConfigureAwait(false); delay = Math.Min(delay * 2, _options.Mqtt.ReconnectMaxDelayMilliseconds); continue; }
            }
            var wake = _signal.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var tick = Task.Delay(TimeSpan.FromMilliseconds(100), _timeProvider, cancellationToken);
            await Task.WhenAny(wake, tick).ConfigureAwait(false);
            while (_signal.Reader.TryRead(out _)) { }
            EnqueuePeriodicDueValues();
            foreach (var pair in _pending.ToArray())
            {
                try
                {
                    if (!MqttPayloadSerializer.TrySerialize(new MqttPayload(1, _options.RuntimeId, pair.Value.Tag.DeviceId, pair.Value.Tag.Id, pair.Value.Tag.Name, pair.Value.Tag.DataType, pair.Value.Value.Value, pair.Value.Value.Quality, pair.Value.Value.Timestamp, _timeProvider.GetUtcNow()), out var payload)) { Interlocked.Increment(ref _rejected); _pending.TryRemove(pair.Key, out _); continue; }
                    var profile = new MqttProfileRegistry(_options.Mqtt.Profiles).All.First(profile => string.Equals(profile.Name, pair.Value.Tag.MqttProfile, StringComparison.OrdinalIgnoreCase));
                    using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); publishCts.CancelAfter(_options.Mqtt.PublishTimeoutMilliseconds);
                    await _transport.PublishAsync(new MqttPublishRequest(pair.Value.Topic, payload, profile.QualityOfService, profile.Retain), publishCts.Token).ConfigureAwait(false);
                    if (_pending.TryGetValue(pair.Key, out var current) && current.Value.Sequence == pair.Value.Value.Sequence) _pending.TryRemove(pair.Key, out _);
                    Interlocked.Increment(ref _published); _lastPublished = _timeProvider.GetUtcNow(); ScheduleNextPeriodic(pair.Value.Tag, profile);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { Interlocked.Increment(ref _failures); SetState(MqttRuntimeState.Offline, "MQTT_PUBLISH", ex.Message); try { await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false); } catch { } await Task.Delay(TimeSpan.FromMilliseconds(delay), _timeProvider, cancellationToken).ConfigureAwait(false); delay = Math.Min(delay * 2, _options.Mqtt.ReconnectMaxDelayMilliseconds); break; }
            }
        }
    }
    private void EnqueuePeriodicDueValues()
    {
        var registry = new MqttProfileRegistry(_options.Mqtt.Profiles);
        var now = _timeProvider.GetUtcNow();
        foreach (var tag in _options.Tags.Where(tag => tag.Enabled && tag.MqttPublishEnabled))
        {
            if (!registry.TryGet(tag.MqttProfile, out var profile) || profile is null || profile.Mode == MqttPublishMode.OnChange || profile.MaximumIntervalMilliseconds <= 0) continue;
            if (_nextPeriodic.TryGetValue(tag.Id, out var due) && due > now) continue;
            if (_tagCache.TryGet(tag.Id, out var value) && value is not null) Queue(tag, value, force: true);
        }
    }
    private void ScheduleNextPeriodic(TagDefinition tag, MqttProfileDefinition profile)
    { if (profile.Mode != MqttPublishMode.OnChange && profile.MaximumIntervalMilliseconds > 0) _nextPeriodic[tag.Id] = _timeProvider.GetUtcNow().AddMilliseconds(profile.MaximumIntervalMilliseconds); }
    private string ResolveClientId() => string.IsNullOrWhiteSpace(_options.Mqtt.ClientId) ? $"scada-{_options.RuntimeId}" : _options.Mqtt.ClientId;
    private bool TryGetPassword(out string? password) { password = null; var reference = _options.Mqtt.PasswordReference; if (string.IsNullOrWhiteSpace(reference)) return true; if (!reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) return false; password = Environment.GetEnvironmentVariable(reference[4..]); return !string.IsNullOrWhiteSpace(password); }
    private void SetState(MqttRuntimeState state, string? code, string? message) { _state = state; _errorCode = code; _errorMessage = message; }
    private sealed record Pending(TagDefinition Tag, TagValue Value, string Topic);
}
