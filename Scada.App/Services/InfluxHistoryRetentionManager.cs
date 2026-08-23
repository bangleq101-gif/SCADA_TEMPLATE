using Scada.Core.History;
using Scada.Infrastructure.History.Influx;

namespace Scada.App.Services;

public sealed class InfluxHistoryRetentionManager : IHistoryRetentionManager
{
    private readonly InfluxRetentionApplier _applier = new();

    public Task<HistoryStoreOperationResult> ApplyAsync(
        InfluxDbOptions candidate,
        CancellationToken cancellationToken) =>
        _applier.ApplyAsync(candidate, cancellationToken);
}
