using Scada.Core.History;
using Scada.Infrastructure.History.Influx;

namespace Scada.App.Services;

public sealed class InfluxHistoryConnectionTester : IHistoryConnectionTester
{
    private readonly InfluxConnectionTester _tester = new();

    public Task<HistoryStoreOperationResult> TestAsync(
        InfluxDbOptions options,
        CancellationToken cancellationToken) =>
        _tester.TestAsync(options, cancellationToken);
}
