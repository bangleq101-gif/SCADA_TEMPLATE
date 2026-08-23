using Scada.Core.History;

namespace Scada.App.Services;

public interface IHistoryRetentionManager
{
    Task<HistoryStoreOperationResult> ApplyAsync(
        InfluxDbOptions candidate,
        CancellationToken cancellationToken);
}
