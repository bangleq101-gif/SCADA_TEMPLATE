using Scada.Core.History;

namespace Scada.Infrastructure.History.Influx;

public sealed class InfluxConnectionTester
{
    public async Task<HistoryStoreOperationResult> TestAsync(
        InfluxDbOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!InfluxSecretResolver.TryResolve(
                options.TokenReference,
                out var token,
                out var errorCode,
                out var errorMessage))
        {
            return new HistoryStoreOperationResult(false, errorCode, errorMessage);
        }

        await using var transport = new InfluxHistoryClient(new InfluxDbClientSettings(
            options.Url,
            options.Organization,
            options.Bucket,
            token!,
            Math.Max(
                options.ConnectionTimeoutMilliseconds,
                Math.Max(options.WriteTimeoutMilliseconds, options.QueryTimeoutMilliseconds))));
        try
        {
            await transport.ProbeAsync(cancellationToken).ConfigureAwait(false);
            return new HistoryStoreOperationResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfluxTransportException exception)
        {
            return new HistoryStoreOperationResult(false, exception.Code, exception.Message);
        }
    }
}
