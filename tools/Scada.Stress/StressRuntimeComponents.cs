using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.History;
using Scada.Core.Tags;

namespace Scada.Stress;

public sealed class StressSimulatorDriver(ValueChangePattern pattern, int seed) : IPlcDriver
{
    private readonly ConcurrentDictionary<string, long> _readsByTag = new(StringComparer.OrdinalIgnoreCase);
    private long _readOperations;
    public string DriverType => "Simulator";
    public long ReadOperations => Interlocked.Read(ref _readOperations);
    public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<IReadOnlyList<DriverReadResult>> ReadAsync(DeviceDefinition device, IReadOnlyList<DriverReadRequest> requests, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _readOperations);
        var timestamp = DateTimeOffset.UtcNow;
        IReadOnlyList<DriverReadResult> results = requests.Select(request =>
        {
            var read = _readsByTag.AddOrUpdate(request.TagId, 1, (_, current) => current + 1);
            var changeIndex = pattern switch { ValueChangePattern.EveryScan => read, ValueChangePattern.EveryFourthRead => (read - 1) / 4, _ => 0 };
            var stable = StableSeed(request.TagId, request.Address, seed);
            object value = request.DataType switch
            {
                TagDataType.Boolean => ((stable + changeIndex) & 1) == 0,
                TagDataType.Int32 => (int)((stable + changeIndex) % int.MaxValue),
                TagDataType.Int64 => stable * 1_000L + changeIndex,
                TagDataType.Double => (stable % 10_000) / 100.0 + changeIndex * .01,
                TagDataType.String => $"S{stable % 1000:000}-{changeIndex % 100:00}",
                _ => throw new ArgumentOutOfRangeException()
            };
            return new DriverReadResult(request.TagId, value, TagQuality.Good, timestamp);
        }).ToArray();
        return Task.FromResult(results);
    }
    public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) => Task.CompletedTask;

    private static uint StableSeed(string tagId, string address, int seed)
    {
        var hash = 2166136261u ^ (uint)seed;
        foreach (var character in string.Concat(tagId, "|", address)) { hash ^= character; hash *= 16777619u; }
        return hash;
    }
}

public sealed class TimedHistoryStore(IHistoryStore inner) : IHistoryStore
{
    private long _batchCount, _sampleCount, _measurementGeneration;
    public BoundedHistogram WriteLatency { get; } = new();
    public long BatchCount => Interlocked.Read(ref _batchCount);
    public long SampleCount => Interlocked.Read(ref _sampleCount);
    public void BeginMeasurement()
    {
        Interlocked.Increment(ref _measurementGeneration);
        Interlocked.Exchange(ref _batchCount, 0);
        Interlocked.Exchange(ref _sampleCount, 0);
        WriteLatency.Reset();
    }
    public Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken) => inner.PreflightAsync(cancellationToken);
    public Task InitializeAsync(CancellationToken cancellationToken) => inner.InitializeAsync(cancellationToken);
    public async Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _measurementGeneration);
        var started = Stopwatch.GetTimestamp();
        try { await inner.WriteBatchAsync(samples, cancellationToken); }
        finally
        {
            if (generation != 0 && generation == Volatile.Read(ref _measurementGeneration))
            {
                Interlocked.Increment(ref _batchCount);
                Interlocked.Add(ref _sampleCount, samples.Count);
                WriteLatency.Record((long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000));
            }
        }
    }
    public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) => inner.QueryAsync(query, cancellationToken);
}
