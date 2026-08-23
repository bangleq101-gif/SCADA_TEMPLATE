using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Scada.Core.History;
using Scada.Infrastructure.History;
using Scada.Infrastructure.Persistence;
using Scada.Runtime.Engine;
using Scada.Runtime.Drivers;
using Scada.Runtime.Historian;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;

namespace Scada.Stress;

public sealed record StressRunSettings(StressProfile Profile, int DeviceCount, int TagsPerDevice, int WarmupSeconds, int MeasurementSeconds, string OutputDirectory, bool InstrumentationEnabled, int Seed = StressWorkloadFactory.DefaultSeed, ValueChangePattern ChangePattern = ValueChangePattern.EveryFourthRead, string PowerMode = "AC", string? RepositoryRoot = null, string? ExpectedGitSha = null, bool Qualification = false);

public sealed class StressScenarioRunner
{
    public async Task<StressRunResult> RunAsync(StressRunSettings settings, CancellationToken cancellationToken)
    {
        var repository = InspectRepository(settings.RepositoryRoot ?? Directory.GetCurrentDirectory());
        if (settings.Qualification)
        {
            if (!settings.InstrumentationEnabled) throw new InvalidOperationException("Qualification requires instrumentation.");
            if (string.IsNullOrWhiteSpace(settings.ExpectedGitSha)) throw new InvalidOperationException("Qualification requires an expected git SHA.");
            var qualification = StressQualificationIdentity.Evaluate(settings.RepositoryRoot ?? string.Empty, settings.ExpectedGitSha, repository.Root, repository.Sha, repository.Clean);
            if (!qualification.IsValid) throw new InvalidOperationException(string.Join(" ", qualification.Violations));
        }
        var workload = StressWorkloadFactory.Create(settings.Profile, settings.DeviceCount, settings.TagsPerDevice, settings.Seed, settings.ChangePattern);
        Directory.CreateDirectory(settings.OutputDirectory);
        var cache = new TagCache(settings.InstrumentationEnabled);
        var pollingMetrics = new PollingMetricsCollector();
        var driver = new StressSimulatorDriver(settings.ChangePattern, settings.Seed);
        var resolver = new DriverResolver([DriverRegistration.Shared("Simulator", driver)]);
        var manager = new DeviceManager(workload.Options, resolver, new TagEngine(cache), NullLogger<DeviceManager>.Instance, NullLogger<DevicePollingWorker>.Instance, TimeProvider.System, settings.InstrumentationEnabled ? pollingMetrics : null);
        var polling = new PollingRuntimeService(manager);

        TimedHistoryStore? timedHistory = null;
        HistorianRuntimeService? historian = null;
        string? databasePath = null;
        if (workload.Options.Historian.Enabled)
        {
            var projectFile = Path.Combine(settings.OutputDirectory, "project.json");
            await File.WriteAllTextAsync(projectFile, "{}", cancellationToken);
            workload.Options.Historian.DatabasePath = "history.db";
            var sqlite = new SqliteHistoryStore(new ProjectPath(projectFile), workload.Options.Historian.DatabasePath);
            timedHistory = new TimedHistoryStore(sqlite);
            historian = new HistorianRuntimeService(workload.Options, cache, timedHistory, NullLogger<HistorianRuntimeService>.Instance, TimeProvider.System);
            databasePath = Path.Combine(settings.OutputDirectory, "history.db");
        }

        StressMqttTransport? mqttTransport = null;
        MqttRuntimeService? mqtt = null;
        if (workload.Options.Mqtt.Enabled)
        {
            mqttTransport = new StressMqttTransport(TimeSpan.Zero);
            mqtt = new MqttRuntimeService(workload.Options, cache, mqttTransport, NullLogger<MqttRuntimeService>.Instance, TimeProvider.System);
        }

        UiStressHost? ui = null;
        if (settings.Profile is StressProfile.UiActive or StressProfile.CombinedWorstCase)
        {
            ui = new UiStressHost(cache, workload.Options);
            await ui.StartAsync();
        }

        var startedAt = DateTimeOffset.UtcNow;
        var shutdownWatch = new Stopwatch();
        var historianDrainMilliseconds = 0d;
        try
        {
            if (historian is not null) await historian.StartAsync(cancellationToken);
            if (mqtt is not null) await mqtt.StartAsync(cancellationToken);
            await polling.StartAsync(cancellationToken);
            if (settings.WarmupSeconds > 0) await DelayWithHeartbeats(settings.WarmupSeconds, ui, cancellationToken);

            pollingMetrics.BeginMeasurement();
            timedHistory?.BeginMeasurement();
            mqttTransport?.BeginMeasurement();
            ui?.BeginMeasurement();
            var cacheStart = cache.Snapshot;
            var historianStart = historian?.Snapshot;
            var mqttStart = mqtt?.Snapshot;
            var persistedRowsStart = databasePath is null ? 0 : await CountRowsAsync(databasePath, cancellationToken);
            var process = new ProcessMetricsSampler();
            var historianSampledHighWater = 0;
            var mqttSampledHighWater = 0;
            for (var second = 0; second < settings.MeasurementSeconds; second++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                ui?.PostHeartbeat(); process.Sample();
                historianSampledHighWater = Math.Max(historianSampledHighWater, historian?.Snapshot.QueueDepth ?? 0);
                mqttSampledHighWater = Math.Max(mqttSampledHighWater, mqtt?.Snapshot.PendingTags ?? 0);
            }

            var elapsedSeconds = Math.Max(0.001, settings.MeasurementSeconds);
            var cacheEnd = cache.Snapshot;
            var pollingEnd = pollingMetrics.Snapshot;
            var processEnd = process.Snapshot();
            var uiEnd = ui?.Snapshot;
            var deviceSnapshots = polling.DeviceStates;
            var historianMeasurementEnd = historian?.Snapshot;
            var mqttMeasurementEnd = mqtt?.Snapshot;

            shutdownWatch.Start();
            await polling.StopAsync(cancellationToken);
            if (mqtt is not null) await mqtt.StopAsync(cancellationToken);
            if (historian is not null)
            {
                var historianDrain = Stopwatch.StartNew();
                await historian.StopAsync(cancellationToken);
                historianDrain.Stop();
                historianDrainMilliseconds = historianDrain.Elapsed.TotalMilliseconds;
            }
            SqliteConnection.ClearAllPools();
            ui?.Dispose(); ui = null;
            shutdownWatch.Stop();
            var historianEnd = historian?.Snapshot;
            var mqttEnd = mqtt?.Snapshot;
            var persistedRows = databasePath is null ? 0 : await CountRowsAsync(databasePath, cancellationToken) - persistedRowsStart;

            var historianServiceWrittenSamples = historianEnd?.WrittenSamples - (historianStart?.WrittenSamples ?? 0) ?? 0;
            var metrics = new StressMetricSummary(processEnd.CpuPercent, processEnd.WorkingSet, (cacheEnd.Updates - cacheStart.Updates) / elapsedSeconds,
                pollingEnd.Jitter.Values.Select(value => value.P95 / 1000.0).DefaultIfEmpty().Max(), (uiEnd?.LatencyMicroseconds.P95 ?? 0) / 1000.0)
            {
                PrivateMemoryBytes = processEnd.PrivateMemory, ManagedHeapBytes = processEnd.ManagedHeap, AllocatedBytes = processEnd.AllocatedBytes,
                Gen0Collections = processEnd.Gen0, Gen1Collections = processEnd.Gen1, Gen2Collections = processEnd.Gen2,
                BatchesPerSecond = pollingEnd.Batches / elapsedSeconds, TagsPerSecond = pollingEnd.Tags / elapsedSeconds,
                ScanDurationMicroseconds = pollingEnd.Duration, ScanJitterMicroseconds = pollingEnd.Jitter,
                MissedCycles = pollingEnd.MissedCycles, PollFailures = pollingEnd.Failures,
                CallbackInvocations = cacheEnd.CallbackInvocations - cacheStart.CallbackInvocations,
                SubscriberExceptions = cacheEnd.SubscriberExceptions - cacheStart.SubscriberExceptions,
                Historian = historianEnd is null ? null : new HistorianStressSummary(historianSampledHighWater,
                    historianEnd.EnqueuedSamples - (historianStart?.EnqueuedSamples ?? 0) + historianEnd.DroppedSamples - (historianStart?.DroppedSamples ?? 0),
                    historianEnd.EnqueuedSamples - (historianStart?.EnqueuedSamples ?? 0), historianServiceWrittenSamples,
                    historianEnd.RejectedSamples - (historianStart?.RejectedSamples ?? 0), historianEnd.DroppedSamples - (historianStart?.DroppedSamples ?? 0),
                    historianEnd.AbandonedSamples - (historianStart?.AbandonedSamples ?? 0), historianEnd.WriteFailures - (historianStart?.WriteFailures ?? 0),
                    timedHistory!.BatchCount, timedHistory.BatchCount / elapsedSeconds, timedHistory.SampleCount,
                    timedHistory.BatchCount == 0 ? 0 : (double)timedHistory.SampleCount / timedHistory.BatchCount,
                    PollingMetricsCollector.Summarize(timedHistory.WriteLatency), historianDrainMilliseconds,
                    databasePath is not null && File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0, persistedRows),
                Mqtt = mqttEnd is null ? null : new MqttStressSummary(mqttEnd.PublishedMessages - (mqttStart?.PublishedMessages ?? 0),
                    (mqttEnd.PublishedMessages - (mqttStart?.PublishedMessages ?? 0)) / elapsedSeconds, mqttSampledHighWater,
                    mqttEnd.CoalescedUpdates - (mqttStart?.CoalescedUpdates ?? 0), mqttEnd.RejectedSamples - (mqttStart?.RejectedSamples ?? 0),
                    mqttEnd.PublishFailures - (mqttStart?.PublishFailures ?? 0), mqttEnd.ReconnectAttempts - (mqttStart?.ReconnectAttempts ?? 0),
                    mqttTransport!.MaximumConcurrency, PollingMetricsCollector.Summarize(mqttTransport.PublishLatency), mqttTransport.SourceTimestampOrderCorrect, 0),
                Dispatcher = uiEnd,
                ShutdownMilliseconds = shutdownWatch.Elapsed.TotalMilliseconds
            };
            var correctness = StressCorrectnessEvaluator.Evaluate(new StressQualificationFacts
            {
                ExpectedDeviceCount = settings.DeviceCount,
                ActualDeviceCount = deviceSnapshots.Count,
                ExpectedTagCount = settings.DeviceCount * settings.TagsPerDevice,
                ActualTagCount = workload.Options.Tags.Count,
                TagCacheValueCount = cacheEnd.ValueCount,
                PollingFailures = pollingEnd.Failures,
                MissedCycles = pollingEnd.MissedCycles,
                HistorianRejected = historianEnd?.RejectedSamples - (historianStart?.RejectedSamples ?? 0) ?? 0,
                HistorianDropped = historianEnd?.DroppedSamples - (historianStart?.DroppedSamples ?? 0) ?? 0,
                HistorianAbandoned = historianEnd?.AbandonedSamples - (historianStart?.AbandonedSamples ?? 0) ?? 0,
                HistorianWriteFailures = historianEnd?.WriteFailures - (historianStart?.WriteFailures ?? 0) ?? 0,
                HistorianPersistedRows = persistedRows,
                HistorianMeasurementSamplesWritten = timedHistory?.SampleCount ?? 0,
                MqttSourceTimestampOrderCorrect = mqttTransport?.SourceTimestampOrderCorrect ?? true,
                MqttPlcReadsCaused = 0,
                MqttFailures = mqttEnd?.PublishFailures - (mqttStart?.PublishFailures ?? 0) ?? 0,
                DispatcherHeartbeatGaps = uiEnd?.HeartbeatGaps ?? 0,
                ActiveSubscriptionsAfterShutdown = cache.Snapshot.SubscriptionCount,
                ShutdownCompleted = true,
                UnhandledSubsystemFailure = deviceSnapshots.Values.Any(snapshot => snapshot.ConnectionState == Scada.Core.Devices.DeviceConnectionState.Faulted) || historianMeasurementEnd?.State == HistorianRuntimeState.Faulted || mqttMeasurementEnd?.State == Scada.Core.Mqtt.MqttRuntimeState.Faulted
            });
            var fingerprint = CreateFingerprint(settings, workload.ConfigurationHash);
            var result = new StressRunResult(StressWorkloadFactory.ResultSchemaVersion, StressWorkloadFactory.WorkloadVersion, StressWorkloadFactory.MeasurementContractVersion, settings.Profile, repository.Sha, repository.Clean, repository.Root, fingerprint.CompatibilityKey, fingerprint,
                new StressWorkloadSummary(settings.DeviceCount, workload.Options.Tags.Count, settings.Seed, settings.ChangePattern,
                    workload.ExpectedTagCacheUpdatesPerSecond, workload.ExpectedValueChangesPerSecond, metrics.UpdatesPerSecond, workload.ConfigurationHash), metrics,
                correctness, startedAt, DateTimeOffset.UtcNow);
            await StressResultWriter.WriteAsync(settings.OutputDirectory, result, cancellationToken);
            return result;
        }
        finally
        {
            ui?.Dispose();
            try { await polling.StopAsync(CancellationToken.None); } catch { }
            if (mqtt is not null) try { await mqtt.StopAsync(CancellationToken.None); } catch { }
            if (historian is not null) try { await historian.StopAsync(CancellationToken.None); } catch { }
            SqliteConnection.ClearAllPools();
            if (mqttTransport is not null) await mqttTransport.DisposeAsync();
        }
    }

    private static async Task DelayWithHeartbeats(int seconds, UiStressHost? ui, CancellationToken cancellationToken)
    { for (var index = 0; index < seconds; index++) { await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); ui?.PostHeartbeat(); } }
    private static ResultFingerprint CreateFingerprint(StressRunSettings settings, string hash) => new(
        Environment.MachineName, Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, Environment.ProcessorCount,
        StressWorkloadFactory.WorkloadVersion, settings.Profile.ToString(), hash, settings.PowerMode, settings.Seed, settings.WarmupSeconds, settings.MeasurementSeconds);
    private static GitRepositoryState InspectRepository(string root)
    {
        var requestedRoot = Path.GetFullPath(root);
        var actualRoot = RunGit(requestedRoot, "rev-parse --show-toplevel");
        var sha = RunGit(actualRoot, "rev-parse HEAD");
        var status = RunGit(actualRoot, "status --porcelain --untracked-files=all");
        return new(actualRoot, sha, string.IsNullOrWhiteSpace(status));
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
        if (process is null) throw new InvalidOperationException("Unable to start git for stress qualification identity.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Git qualification identity check failed: {error}");
        return output;
    }

    private sealed record GitRepositoryState(string Root, string Sha, bool Clean);

    private static async Task<long> CountRowsAsync(string databasePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath)) return 0;
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'HistorySamples';";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0) return 0;
        command.CommandText = "SELECT COUNT(*) FROM HistorySamples;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
