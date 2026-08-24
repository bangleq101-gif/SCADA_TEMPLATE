using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Scada.Core.Mqtt;

namespace Scada.Stress;

public sealed class StressMqttTransport(TimeSpan latency) : IMqttTransport
{
    private int _connected, _measurementConcurrency, _maximumConcurrency;
    private long _published;
    private long _measurementGeneration;
    private int _sourceTimestampOrderCorrect = 1;
    public bool IsConnected => Volatile.Read(ref _connected) != 0;
    public long MeasurementPublishedCount => Interlocked.Read(ref _published);
    public long PublishedCount => MeasurementPublishedCount;
    public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);
    public BoundedHistogram PublishLatency { get; } = new();
    public ConcurrentDictionary<string, DateTimeOffset> LatestSourceTimestampByTopic { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, byte[]> LatestPayloadByTopic { get; } = new(StringComparer.Ordinal);
    public bool SourceTimestampOrderCorrect => Volatile.Read(ref _sourceTimestampOrderCorrect) != 0;

    public void BeginMeasurement()
    {
        Interlocked.Increment(ref _measurementGeneration);
        Interlocked.Exchange(ref _published, 0);
        Interlocked.Exchange(ref _measurementConcurrency, 0);
        Interlocked.Exchange(ref _maximumConcurrency, 0);
        Volatile.Write(ref _sourceTimestampOrderCorrect, 1);
        PublishLatency.Reset();
        LatestSourceTimestampByTopic.Clear();
        LatestPayloadByTopic.Clear();
    }

    public Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Volatile.Write(ref _connected, 1); return Task.FromResult(new MqttConnectionResult(true)); }
    public async Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _measurementGeneration);
        var isMeasurement = generation != 0;
        var concurrent = isMeasurement ? Interlocked.Increment(ref _measurementConcurrency) : 0;
        if (isMeasurement) Max(ref _maximumConcurrency, concurrent);
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (latency > TimeSpan.Zero) await Task.Delay(latency, cancellationToken);
            using var document = JsonDocument.Parse(request.Payload);
            var timestamp = default(DateTimeOffset);
            var hasSourceTimestamp = document.RootElement.TryGetProperty("sourceTimestampUtc", out var timestampElement) && timestampElement.TryGetDateTimeOffset(out timestamp);
            if (generation == Volatile.Read(ref _measurementGeneration) && isMeasurement && !hasSourceTimestamp)
            {
                Volatile.Write(ref _sourceTimestampOrderCorrect, 0);
            }
            else if (generation == Volatile.Read(ref _measurementGeneration) && isMeasurement && hasSourceTimestamp)
            {
                LatestSourceTimestampByTopic.AddOrUpdate(request.Topic, timestamp, (_, current) =>
                {
                    if (timestamp < current) Volatile.Write(ref _sourceTimestampOrderCorrect, 0);
                    return timestamp > current ? timestamp : current;
                });
            }
            if (generation == Volatile.Read(ref _measurementGeneration) && isMeasurement)
            {
                LatestPayloadByTopic[request.Topic] = request.Payload.ToArray();
                Interlocked.Increment(ref _published);
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _measurementGeneration) && isMeasurement)
            {
                PublishLatency.Record((long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000));
                Interlocked.Decrement(ref _measurementConcurrency);
            }
        }
    }
    public Task DisconnectAsync(CancellationToken cancellationToken) { Volatile.Write(ref _connected, 0); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Volatile.Write(ref _connected, 0); return ValueTask.CompletedTask; }
    private static void Max(ref int target, int value) { int current; while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { } }
}
