using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using InfluxDB.Client.Core.Exceptions;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Infrastructure.History.Influx;
using Scada.Infrastructure.Persistence;
using Xunit;

namespace Scada.Infrastructure.Tests;

public sealed class InfluxProviderTests
{
    [Fact]
    public async Task OutboxIsTypedIdempotentAndTimestampCounterSurvivesAckAndClear()
    {
        var directory = CreateTempDirectory();
        try
        {
            var projectPath = new ProjectPath(Path.Combine(directory, "project.json"));
            await using var outbox = new InfluxOutboxStore(projectPath, "Data/influx-buffer.db");
            await outbox.InitializeAsync(CancellationToken.None);
            var first = Sample("T1", TagDataType.Double, 1.25d, sequence: 1);
            var fingerprint = "A";

            var append = await outbox.AppendAsync([first], fingerprint, 10, DateTimeOffset.UtcNow, CancellationToken.None);
            var duplicate = await outbox.AppendAsync([first], fingerprint, 10, DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.Equal(1, append.AcceptedCount);
            Assert.Equal(1, duplicate.DuplicateCount);
            var firstRow = Assert.Single(await outbox.ReadPendingAsync(fingerprint, 10, CancellationToken.None));
            await outbox.AcknowledgeAsync(fingerprint, [firstRow.Id], DateTimeOffset.UtcNow, CancellationToken.None);
            await outbox.ClearDestinationBufferAsync(fingerprint, CancellationToken.None);

            var second = first with { TagSequence = 2 };
            await outbox.AppendAsync([second], fingerprint, 10, DateTimeOffset.UtcNow, CancellationToken.None);
            var secondRow = Assert.Single(await outbox.ReadPendingAsync(fingerprint, 10, CancellationToken.None));

            Assert.True(secondRow.RemoteTimestampNanoseconds > firstRow.RemoteTimestampNanoseconds);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task InvalidTimestampIsTerminalAndDoesNotBlockValidSamples()
    {
        var directory = CreateTempDirectory();
        try
        {
            var projectPath = new ProjectPath(Path.Combine(directory, "project.json"));
            await using var outbox = new InfluxOutboxStore(projectPath, "Data/influx-buffer.db");
            await outbox.InitializeAsync(CancellationToken.None);
            var invalid = Sample("BAD", TagDataType.Double, 2d, sequence: 1) with
            {
                RecordedAtUtc = DateTimeOffset.MaxValue
            };
            var valid = Sample("GOOD", TagDataType.Double, 3d, sequence: 2);

            var result = await outbox.AppendAsync(
                [invalid, valid],
                "A",
                10,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            var diagnostics = await outbox.ReadDiagnosticsAsync("A", CancellationToken.None);

            Assert.Equal(1, result.AcceptedCount);
            Assert.Contains(result.Rejections, rejection => rejection.ErrorCode == "INFLUX_TIMESTAMP_OUT_OF_RANGE");
            Assert.Equal(1, diagnostics.RemoteRejectedSamples);
            Assert.Equal("GOOD", Assert.Single(await outbox.ReadPendingAsync("A", 10, CancellationToken.None)).Sample.TagId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task CapacityCountsAllDestinationsButNotDuplicateKeys()
    {
        var directory = CreateTempDirectory();
        try
        {
            var projectPath = new ProjectPath(Path.Combine(directory, "project.json"));
            await using var outbox = new InfluxOutboxStore(projectPath, "Data/influx-buffer.db");
            await outbox.InitializeAsync(CancellationToken.None);
            var first = Sample("T1", TagDataType.Double, 1d);

            await outbox.AppendAsync([first], "A", 1, DateTimeOffset.UtcNow, CancellationToken.None);
            var duplicate = await outbox.AppendAsync([first], "A", 1, DateTimeOffset.UtcNow, CancellationToken.None);
            var full = await Assert.ThrowsAsync<HistoryStoreTransientException>(() =>
                outbox.AppendAsync([first with { TagId = "T2" }], "B", 1, DateTimeOffset.UtcNow, CancellationToken.None));
            var diagnostics = await outbox.ReadDiagnosticsAsync("A", CancellationToken.None);

            Assert.Equal(1, duplicate.DuplicateCount);
            Assert.Equal("INFLUX_BUFFER_FULL", full.Code);
            Assert.Equal(0, diagnostics.BufferFullRejections);
            Assert.Equal(1, diagnostics.PendingSamples);
            Assert.Equal(0, diagnostics.OrphanedDestinationSamples);

            var destinationB = await outbox.ReadDiagnosticsAsync("B", CancellationToken.None);
            Assert.Equal(1, destinationB.BufferFullRejections);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task DestinationBuffersCanBeClearedIndependently()
    {
        var directory = CreateTempDirectory();
        try
        {
            var projectPath = new ProjectPath(Path.Combine(directory, "project.json"));
            await using var outbox = new InfluxOutboxStore(projectPath, "Data/influx-buffer.db");
            await outbox.InitializeAsync(CancellationToken.None);
            await outbox.AppendAsync([Sample("A", TagDataType.Double, 1d)], "A", 10, DateTimeOffset.UtcNow, CancellationToken.None);
            await outbox.AppendAsync([Sample("B", TagDataType.Double, 2d)], "B", 10, DateTimeOffset.UtcNow, CancellationToken.None);

            var before = await outbox.ReadDiagnosticsAsync("B", CancellationToken.None);
            await outbox.ClearDestinationBufferAsync("B", CancellationToken.None);
            var afterCurrentClear = await outbox.ReadDiagnosticsAsync("B", CancellationToken.None);
            await outbox.ClearDestinationBufferAsync("A", CancellationToken.None);
            var afterAllClear = await outbox.ReadDiagnosticsAsync("B", CancellationToken.None);

            Assert.Equal(1, before.PendingSamples);
            Assert.Equal(1, before.OrphanedDestinationSamples);
            Assert.Equal(0, afterCurrentClear.PendingSamples);
            Assert.Equal(1, afterCurrentClear.OrphanedDestinationSamples);
            Assert.Equal(0, afterAllClear.PendingSamples);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void PointMapperUsesOnlyApprovedTagsAndTypedFields()
    {
        var sample = Sample("T,1", TagDataType.String, "a\"b", TagQuality.Disconnected, sequence: 7);
        var row = new InfluxOutboxRow(1, "key", "fingerprint", sample, 123, DateTimeOffset.UtcNow);

        var line = InfluxHistoryPointMapper.ToLineProtocol(row, "scada history");

        Assert.StartsWith("scada\\ history,runtime_id=Runtime01,tag_id=T\\,1 ", line, StringComparison.Ordinal);
        Assert.Contains("data_type=\"String\"", line, StringComparison.Ordinal);
        Assert.Contains("quality=\"Disconnected\"", line, StringComparison.Ordinal);
        Assert.Contains("value_text=\"a\\\"b\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain(",sequence=", line, StringComparison.Ordinal);
        Assert.DoesNotContain(",value=", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidLineProtocolInputIsRejectedAsTerminalBeforeRemoteSync()
    {
        var directory = CreateTempDirectory();
        try
        {
            var projectPath = new ProjectPath(Path.Combine(directory, "project.json"));
            await using var outbox = new InfluxOutboxStore(projectPath, "Data/influx-buffer.db");
            await outbox.InitializeAsync(CancellationToken.None);

            var newline = await outbox.AppendAsync(
                [Sample("T1", TagDataType.String, "line1\nline2")],
                "A",
                10,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            var controlTag = await outbox.AppendAsync(
                [Sample("T\t2", TagDataType.Double, 1d)],
                "A",
                10,
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            Assert.Contains(newline.Rejections, rejection => rejection.ErrorCode == "INFLUX_STRING_NEWLINE_UNSUPPORTED");
            Assert.Contains(controlTag.Rejections, rejection => rejection.ErrorCode == "INFLUX_TAG_CONTROL_CHAR");
            var diagnostics = await outbox.ReadDiagnosticsAsync("A", CancellationToken.None);
            Assert.Equal(2, diagnostics.RemoteRejectedSamples);
            Assert.Empty(await outbox.ReadPendingAsync("A", 10, CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ProductionExceptionTranslationUsesOfficialInfluxStatusModel()
    {
        var cases = new (InfluxException Exception, int Status, string Code)[]
        {
            (new UnauthorizedException("unauthorized", new Exception()), 401, "INFLUX_PERMISSION_DENIED"),
            (new ForbiddenException("forbidden", new Exception()), 403, "INFLUX_PERMISSION_DENIED"),
            (new NotFoundException("not found", new Exception()), 404, "INFLUX_NOT_FOUND"),
            (new TooManyRequestsException("rate limited", new Exception()), 429, "INFLUX_RATE_LIMITED"),
            (new InternalServerErrorException("server error", new Exception()), 500, "INFLUX_REMOTE_SERVER_ERROR"),
            (new BadRequestException("bad request", new Exception()), 400, "INFLUX_BAD_REQUEST")
        };

        foreach (var testCase in cases)
        {
            var translated = InfluxHistoryClient.Translate(testCase.Exception);

            Assert.Equal(testCase.Code, translated.Code);
            Assert.Equal(testCase.Status, translated.StatusCode);
            Assert.False(translated.IsPointSpecific);
        }

        var unavailable = InfluxHistoryClient.Translate(new HttpRequestException("network unavailable"));
        Assert.Equal("INFLUX_REMOTE_UNAVAILABLE", unavailable.Code);
        Assert.Null(unavailable.StatusCode);
    }

    [Fact]
    public void ClientSettingsKeepOperationTimeoutsIndependent()
    {
        var settings = new InfluxDbClientSettings("http://localhost:8086", "org", "bucket", "token", 101, 202, 303);

        Assert.Equal(101, settings.ConnectionTimeoutMilliseconds);
        Assert.Equal(202, settings.WriteTimeoutMilliseconds);
        Assert.Equal(303, settings.QueryTimeoutMilliseconds);
    }

    [Fact]
    public async Task MissingTokenKeepsLocalBufferOperational()
    {
        var directory = CreateTempDirectory();
        try
        {
            var options = CreateInfluxOptions();
            options.TokenReference = "env:SCADA_TEST_MISSING_INFLUX_TOKEN";
            Environment.SetEnvironmentVariable("SCADA_TEST_MISSING_INFLUX_TOKEN", null);
            await using var store = CreateStore(directory, options);
            await store.InitializeAsync(CancellationToken.None);
            await store.WriteBatchAsync([Sample("T1", TagDataType.Double, 1d)], CancellationToken.None);

            await WaitUntilAsync(() =>
                store.Snapshot.State == HistoryStoreState.ConfigurationRequired &&
                store.Snapshot.PendingSamples == 1);

            Assert.Equal(1, store.Snapshot.PendingSamples);
            Assert.Equal("INFLUX_TOKEN_REQUIRED", store.Snapshot.LastErrorCode);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task OnlineTransportAcksDurableRowsWithoutAThirdQueue()
    {
        var directory = CreateTempDirectory();
        try
        {
            var transport = new FakeInfluxTransport();
            await using var store = CreateStore(directory, CreateInfluxOptions(), transport);
            await store.InitializeAsync(CancellationToken.None);
            await store.WriteBatchAsync([Sample("T1", TagDataType.Double, 1d)], CancellationToken.None);

            await WaitUntilAsync(() => transport.WriteCount > 0 && store.Snapshot.PendingSamples == 0);

            Assert.Equal(1, transport.ProbeCount > 0 ? 1 : 0);
            Assert.Single(transport.WrittenLines);
            Assert.Equal(1, store.Snapshot.SyncedSamples);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task GlobalBadRequestPreservesRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var transport = new FakeInfluxTransport
            {
                WriteFailure = _ => new InfluxTransportException("INFLUX_BAD_REQUEST", "global request rejected", 400)
            };
            await using var store = CreateStore(directory, CreateInfluxOptions(), transport);
            await store.InitializeAsync(CancellationToken.None);
            await store.WriteBatchAsync([Sample("T1", TagDataType.Double, 1d)], CancellationToken.None);

            await WaitUntilAsync(() => store.Snapshot.SyncFailures > 0);

            Assert.Equal(1, store.Snapshot.PendingSamples);
            Assert.Equal(0, store.Snapshot.RemoteRejectedSamples);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task PointSpecificBadRequestIsBoundedAndDoesNotBlockFollowingRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var transport = new FakeInfluxTransport
            {
                WriteFailure = lines => lines.Any(line => line.Contains("tag_id=BAD", StringComparison.Ordinal))
                    ? new InfluxTransportException("INFLUX_POINT_REJECTED", "point rejected", 400, pointSpecific: true)
                    : null
            };
            await using var store = CreateStore(directory, CreateInfluxOptions(), transport);
            await store.InitializeAsync(CancellationToken.None);
            await store.WriteBatchAsync(
                [
                    Sample("BAD", TagDataType.Double, 1d, sequence: 1),
                    Sample("GOOD", TagDataType.Double, 2d, sequence: 2)
                ],
                CancellationToken.None);

            await WaitUntilAsync(() => store.Snapshot.RemoteRejectedSamples == 1 && store.Snapshot.PendingSamples == 0);

            Assert.Equal(1, store.Snapshot.SyncedSamples);
            Assert.Contains(transport.WrittenLines, line => line.Contains("tag_id=GOOD", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task QueryUsesWidenedStopAfterRecordedTimestampRollback()
    {
        var directory = CreateTempDirectory();
        try
        {
            var transport = new FakeInfluxTransport
            {
                QueryResponse = """
#group,false,false,false,false,false,false,false,false,false,false,false,false
#datatype,string,long,dateTime:RFC3339,string,string,string,string,string,boolean,long,long,long,double
#default,_result,,,,,,,,,,,,
,result,table,_time,_measurement,runtime_id,tag_id,data_type,quality,has_value,source_timestamp_utc_ticks,recorded_at_utc_ticks,tag_sequence,value_real
,_result,0,2026-01-01T00:00:00Z,scada_history,Runtime01,T1,Double,Good,true,639028224000000000,639028224010000000,1,1.5
"""
            };
            var options = CreateInfluxOptions();
            options.HealthProbeIntervalMilliseconds = 60_000;
            await using var store = CreateStore(directory, options, transport);
            await store.InitializeAsync(CancellationToken.None);
            var recorded = DateTimeOffset.Parse("2026-01-01T00:00:01Z");
            var futureRecorded = recorded.AddMinutes(1);
            await store.WriteBatchAsync([Sample("T1", TagDataType.Double, 1.5d, recorded: futureRecorded)], CancellationToken.None);
            var expectedMinimum = InfluxPointTimestamp.TryGetBaseNanoseconds(futureRecorded, out var baseNs)
                ? baseNs + 1
                : 0;

            var results = await store.QueryAsync(
                new HistoryQuery("Runtime01", "T1", recorded.AddSeconds(-1), recorded.AddSeconds(1), 10),
                CancellationToken.None);

            Assert.Single(results);
            Assert.Contains($"stop: time(v: {expectedMinimum}ns)", transport.LastQuery, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task QueryClampsBelowMinimumAndRejectsRangesOutsideInfluxBounds()
    {
        var directory = CreateTempDirectory();
        try
        {
            var transport = new FakeInfluxTransport();
            await using var store = CreateStore(directory, CreateInfluxOptions(), transport);
            await store.InitializeAsync(CancellationToken.None);

            var belowMinimum = DateTimeOffset.Parse("1600-01-01T00:00:00Z");
            var normal = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            await store.QueryAsync(
                new HistoryQuery("Runtime01", "T1", belowMinimum, normal, 10),
                CancellationToken.None);
            Assert.Contains($"range(start: time(v: {InfluxPointTimestamp.MinNanoseconds}ns)", transport.LastQuery, StringComparison.Ordinal);

            var queryCount = transport.QueryCount;
            await store.QueryAsync(
                new HistoryQuery("Runtime01", "T1", belowMinimum, belowMinimum.AddMinutes(1), 10),
                CancellationToken.None);
            await store.QueryAsync(
                new HistoryQuery("Runtime01", "T1", DateTimeOffset.Parse("2300-01-01T00:00:00Z"), DateTimeOffset.Parse("2300-01-01T00:01:00Z"), 10),
                CancellationToken.None);
            Assert.Equal(queryCount, transport.QueryCount);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ClearCurrentBufferWaitsForAnInFlightRemoteSync()
    {
        var directory = CreateTempDirectory();
        try
        {
            var writeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new FakeInfluxTransport
            {
                WriteStarted = writeStarted,
                AllowWrite = allowWrite
            };
            await using var store = CreateStore(directory, CreateInfluxOptions(), transport);
            await store.InitializeAsync(CancellationToken.None);
            await store.WriteBatchAsync([Sample("T1", TagDataType.Double, 1d)], CancellationToken.None);

            await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var clearTask = store.ClearCurrentBufferAsync(CancellationToken.None);
            await Task.Delay(20);
            Assert.False(clearTask.IsCompleted);

            allowWrite.SetResult(true);
            var clear = await clearTask;
            Assert.True(clear.Succeeded);
            Assert.Equal(0, store.Snapshot.PendingSamples);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void WorkSignalIsCoalescedToOneWakeup()
    {
        using var signal = new SemaphoreSlim(0, 1);

        for (var index = 0; index < 100; index++)
        {
            BufferedInfluxHistoryStore.TryReleaseWorkSignal(signal);
        }

        Assert.Equal(1, signal.CurrentCount);
        signal.Wait();
        Assert.Equal(0, signal.CurrentCount);
    }

    private static BufferedInfluxHistoryStore CreateStore(
        string directory,
        InfluxDbOptions options,
        IInfluxTransport? transport = null)
    {
        var projectPath = new ProjectPath(Path.Combine(directory, "project.json"));
        var historian = new HistorianOptions
        {
            StorageProvider = HistoryStorageProvider.InfluxDb2,
            Influx = options
        };
        return new BufferedInfluxHistoryStore(
            projectPath,
            historian,
            NullLogger<BufferedInfluxHistoryStore>.Instance,
            TimeProvider.System,
            transport);
    }

    private static InfluxDbOptions CreateInfluxOptions() => new()
    {
        Organization = "org",
        Bucket = "bucket",
        Measurement = "scada_history",
        BufferPath = "Data/influx-buffer.db",
        MaxBufferedSamples = 100,
        SyncBatchSize = 8,
        SyncIntervalMilliseconds = 10,
        HealthProbeIntervalMilliseconds = 25,
        ReconnectInitialDelayMilliseconds = 10,
        ReconnectMaxDelayMilliseconds = 100
    };

    private static HistorySample Sample(
        string tagId,
        TagDataType dataType,
        object? value,
        TagQuality quality = TagQuality.Good,
        DateTimeOffset? source = null,
        DateTimeOffset? recorded = null,
        long sequence = 1) => new(
        "Runtime01",
        tagId,
        dataType,
        value,
        quality,
        source ?? DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        recorded ?? DateTimeOffset.Parse("2026-01-01T00:00:01Z"),
        sequence);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition(), "The asynchronous provider condition was not reached in time.");
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScadaM6", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeInfluxTransport : IInfluxTransport
    {
        private readonly List<string> _writtenLines = [];

        public int ProbeCount { get; private set; }
        public int WriteCount { get; private set; }
        public int QueryCount { get; private set; }
        public string LastQuery { get; private set; } = string.Empty;
        public string QueryResponse { get; set; } = string.Empty;
        public Func<IReadOnlyList<string>, InfluxTransportException?>? WriteFailure { get; init; }
        public TaskCompletionSource<bool>? WriteStarted { get; init; }
        public TaskCompletionSource<bool>? AllowWrite { get; init; }
        public IReadOnlyList<string> WrittenLines => _writtenLines;

        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            ProbeCount++;
            return Task.CompletedTask;
        }

        public async Task WriteLinesAsync(
            IReadOnlyList<string> lineProtocolRecords,
            string bucket,
            string organization,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            WriteStarted?.TrySetResult(true);
            if (AllowWrite is not null)
            {
                await AllowWrite.Task.WaitAsync(cancellationToken);
            }

            var failure = WriteFailure?.Invoke(lineProtocolRecords);
            if (failure is not null)
            {
                throw failure;
            }

            _writtenLines.AddRange(lineProtocolRecords);
        }

        public Task<string> QueryRawAsync(string flux, string organization, CancellationToken cancellationToken)
        {
            QueryCount++;
            LastQuery = flux;
            return Task.FromResult(QueryResponse);
        }

        public Task<InfluxRetentionInfo> ReadRetentionAsync(string organization, string bucket, CancellationToken cancellationToken) =>
            Task.FromResult(new InfluxRetentionInfo(null, "bucket-id"));

        public Task ApplyRetentionAsync(string organization, string bucket, long retentionSeconds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
