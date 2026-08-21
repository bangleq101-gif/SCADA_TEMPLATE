using Scada.Core.Devices;

namespace Scada.Runtime.Devices;

public sealed record DeviceRuntimeSnapshot(
    string DeviceId,
    DeviceConnectionState ConnectionState,
    string? LastError,
    DateTimeOffset? LastSuccessfulRead,
    DateTimeOffset? LastFailureAt,
    long ReadCount,
    long FailureCount,
    DateTimeOffset? LastScanStartedAt,
    DateTimeOffset? LastScanCompletedAt,
    TimeSpan? LastScanDuration,
    long MissedCycleCount);
