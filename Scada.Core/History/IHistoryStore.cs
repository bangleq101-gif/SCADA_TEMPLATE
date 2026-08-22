namespace Scada.Core.History;

public interface IHistoryStore
{
    Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken);

    Task InitializeAsync(CancellationToken cancellationToken);

    Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken);

    Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken);
}
