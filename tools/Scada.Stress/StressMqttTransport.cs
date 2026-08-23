using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Scada.Core.Mqtt;

namespace Scada.Stress;

public sealed class StressMqttTransport(TimeSpan latency) : IMqttTransport
{
    private int _connected, _concurrency, _maximumConcurrency;
    private long _published;
    private int _latestSequenceCorrect = 1;
    public bool IsConnected => Volatile.Read(ref _connected) != 0;
    public long PublishedCount => Interlocked.Read(ref _published);
    public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);
    public BoundedHistogram PublishLatency { get; } = new();
    public ConcurrentDictionary<string, long> LatestSequenceByTopic { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, DateTimeOffset> LatestTimestampByTopic { get; } = new(StringComparer.Ordinal);
    public bool LatestSequenceCorrect => Volatile.Read(ref _latestSequenceCorrect) != 0;

    public Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Volatile.Write(ref _connected, 1); return Task.FromResult(new MqttConnectionResult(true)); }
    public async Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken)
    {
        var concurrent = Interlocked.Increment(ref _concurrency);
        Max(ref _maximumConcurrency, concurrent);
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (latency > TimeSpan.Zero) await Task.Delay(latency, cancellationToken);
            using var document = JsonDocument.Parse(request.Payload);
            if (document.RootElement.TryGetProperty("sequence", out var sequence) || document.RootElement.TryGetProperty("tagSequence", out sequence))
                LatestSequenceByTopic.AddOrUpdate(request.Topic, sequence.GetInt64(), (_, current) => Math.Max(current, sequence.GetInt64()));
            if (document.RootElement.TryGetProperty("sourceTimestampUtc", out var timestampElement) && timestampElement.TryGetDateTimeOffset(out var timestamp))
            {
                LatestTimestampByTopic.AddOrUpdate(request.Topic, timestamp, (_, current) =>
                {
                    if (timestamp < current) Volatile.Write(ref _latestSequenceCorrect, 0);
                    return timestamp > current ? timestamp : current;
                });
            }
            Interlocked.Increment(ref _published);
        }
        finally
        {
            PublishLatency.Record((long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000));
            Interlocked.Decrement(ref _concurrency);
        }
    }
    public Task DisconnectAsync(CancellationToken cancellationToken) { Volatile.Write(ref _connected, 0); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Volatile.Write(ref _connected, 0); return ValueTask.CompletedTask; }
    private static void Max(ref int target, int value) { int current; while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { } }
}
