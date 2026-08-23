using Scada.Core.Configuration;
using Scada.Core.History;

namespace Scada.Infrastructure.History.Influx;

public sealed class InfluxRetentionApplier
{
    public async Task<HistoryStoreOperationResult> ApplyAsync(
        InfluxDbOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var validationIssue = RuntimeOptionsValidation.CollectInfluxIssues(options)
            .FirstOrDefault(issue => issue.IsBlocking);
        if (validationIssue is not null)
        {
            return new HistoryStoreOperationResult(false, validationIssue.Code, validationIssue.Message);
        }

        if (!InfluxSecretResolver.TryResolve(
                options.TokenReference,
                out var token,
                out var errorCode,
                out var errorMessage))
        {
            return new HistoryStoreOperationResult(false, errorCode, errorMessage);
        }

        try
        {
            await using var transport = new InfluxHistoryClient(new InfluxDbClientSettings(
                options.Url,
                options.Organization,
                options.Bucket,
                token!,
                options.ConnectionTimeoutMilliseconds,
                options.WriteTimeoutMilliseconds,
                options.QueryTimeoutMilliseconds));
            await transport.ApplyRetentionAsync(
                    options.Organization,
                    options.Bucket,
                    options.RetentionSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
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
        catch (Exception)
        {
            return new HistoryStoreOperationResult(
                false,
                "INFLUX_RETENTION_FAILED",
                "InfluxDB retention management failed before a usable response was received.");
        }
    }
}
