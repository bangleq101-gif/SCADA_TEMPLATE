using Scada.Core.History;

namespace Scada.App.Services;

public interface IHistoryConnectionTester
{
    Task<HistoryStoreOperationResult> TestAsync(
        InfluxDbOptions options,
        CancellationToken cancellationToken);
}
