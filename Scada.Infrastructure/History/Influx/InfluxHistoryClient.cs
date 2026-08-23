using System.Net.Http;
using System.Net.Sockets;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Exceptions;

namespace Scada.Infrastructure.History.Influx;

public sealed class InfluxHistoryClient : IInfluxTransport
{
    private readonly IInfluxDBClient _client;
    private readonly IWriteApiAsync _writeApi;
    private readonly IQueryApi _queryApi;
    private readonly InfluxDbClientSettings _settings;
    private bool _disposed;

    public InfluxHistoryClient(InfluxDbClientSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var clientOptions = new InfluxDBClientOptions(settings.Url)
        {
            Token = settings.Token,
            Org = settings.Organization,
            Bucket = settings.Bucket,
            Timeout = TimeSpan.FromMilliseconds(Math.Max(
                settings.ConnectionTimeoutMilliseconds,
                Math.Max(settings.WriteTimeoutMilliseconds, settings.QueryTimeoutMilliseconds)))
        };

        _client = new InfluxDBClient(clientOptions);
        _writeApi = _client.GetWriteApiAsync(null);
        _queryApi = _client.GetQueryApi(null);
    }

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operationCts = CreateOperationCts(
            _settings.ConnectionTimeoutMilliseconds,
            cancellationToken);
        try
        {
            await _client.PingAsync().WaitAsync(operationCts.Token).ConfigureAwait(false);
            var buckets = await _client.GetBucketsApi()
                .FindBucketsByOrgNameAsync(_settings.Organization, operationCts.Token)
                .ConfigureAwait(false);
            if (buckets is null || !buckets.Any(bucket =>
                    string.Equals(bucket.Name, _settings.Bucket, StringComparison.Ordinal)))
            {
                throw new InfluxTransportException(
                    "INFLUX_BUCKET_NOT_FOUND",
                    "The configured InfluxDB organization or bucket could not be resolved.",
                    404);
            }
        }
        catch (OperationCanceledException) when (
            operationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new InfluxTransportException(
                "INFLUX_CONNECTION_TIMEOUT",
                "InfluxDB connection probe exceeded its configured timeout.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfluxTransportException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Translate(exception);
        }
    }

    public async Task WriteLinesAsync(
        IReadOnlyList<string> lineProtocolRecords,
        string bucket,
        string organization,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operationCts = CreateOperationCts(
            _settings.WriteTimeoutMilliseconds,
            cancellationToken);
        try
        {
            await _writeApi.WriteRecordsAsync(
                lineProtocolRecords,
                WritePrecision.Ns,
                bucket,
                organization,
                operationCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            operationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new InfluxTransportException(
                "INFLUX_WRITE_TIMEOUT",
                "InfluxDB write exceeded its configured timeout.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Translate(exception);
        }
    }

    public async Task<string> QueryRawAsync(
        string flux,
        string organization,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operationCts = CreateOperationCts(
            _settings.QueryTimeoutMilliseconds,
            cancellationToken);
        try
        {
            var query = new Query
            {
                _Query = flux,
                Type = Query.TypeEnum.Flux
            };
            return await _queryApi.QueryRawAsync(query, organization, operationCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            operationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new InfluxTransportException(
                "INFLUX_QUERY_TIMEOUT",
                "InfluxDB query exceeded its configured timeout.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Translate(exception);
        }
    }

    public async Task<InfluxRetentionInfo> ReadRetentionAsync(
        string organization,
        string bucket,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operationCts = CreateOperationCts(
            _settings.ConnectionTimeoutMilliseconds,
            cancellationToken);
        try
        {
            var buckets = await _client.GetBucketsApi()
                .FindBucketsByOrgNameAsync(organization, operationCts.Token)
                .ConfigureAwait(false);
            var match = buckets?.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, bucket, StringComparison.Ordinal));
            if (match is null)
            {
                throw new InfluxTransportException(
                    "INFLUX_BUCKET_NOT_FOUND",
                    "The configured InfluxDB organization or bucket could not be resolved.",
                    404);
            }

            var retention = match.RetentionRules?.FirstOrDefault(rule =>
                rule.Type == BucketRetentionRules.TypeEnum.Expire);
            return new InfluxRetentionInfo(retention?.EverySeconds, match.Id);
        }
        catch (OperationCanceledException) when (
            operationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new InfluxTransportException(
                "INFLUX_CONNECTION_TIMEOUT",
                "InfluxDB retention lookup exceeded its configured timeout.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfluxTransportException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Translate(exception);
        }
    }

    public async Task ApplyRetentionAsync(
        string organization,
        string bucket,
        long retentionSeconds,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operationCts = CreateOperationCts(
            _settings.ConnectionTimeoutMilliseconds,
            cancellationToken);
        try
        {
            var buckets = await _client.GetBucketsApi()
                .FindBucketsByOrgNameAsync(organization, operationCts.Token)
                .ConfigureAwait(false);
            var match = buckets?.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, bucket, StringComparison.Ordinal));
            if (match is null)
            {
                throw new InfluxTransportException(
                    "INFLUX_BUCKET_NOT_FOUND",
                    "The configured InfluxDB organization or bucket could not be resolved.",
                    404);
            }

            match.RetentionRules = retentionSeconds == 0
                ? []
                : [new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, retentionSeconds, null)];
            await _client.GetBucketsApi().UpdateBucketAsync(match, operationCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            operationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new InfluxTransportException(
                "INFLUX_CONNECTION_TIMEOUT",
                "InfluxDB retention update exceeded its configured timeout.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfluxTransportException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Translate(exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_writeApi is IDisposable disposableWriteApi)
            {
                disposableWriteApi.Dispose();
            }

            if (_client is IDisposable disposableClient)
            {
                disposableClient.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    internal static InfluxTransportException Translate(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is InfluxTransportException transportException)
        {
            return transportException;
        }

        if (ContainsUnavailableTransport(exception))
        {
            return new InfluxTransportException(
                "INFLUX_REMOTE_UNAVAILABLE",
                "InfluxDB did not return an HTTP response.",
                innerException: exception);
        }

        if (exception is TimeoutException)
        {
            return new InfluxTransportException(
                "INFLUX_REMOTE_UNAVAILABLE",
                "InfluxDB request timed out before a response was received.",
                innerException: exception);
        }

        if (exception is not InfluxException influxException)
        {
            return new InfluxTransportException(
                "INFLUX_REMOTE_REQUEST_FAILED",
                "InfluxDB request failed; see the provider diagnostics for the sanitized error code.",
                innerException: exception);
        }

        var statusCode = influxException.Status > 0 ? (int?)influxException.Status : null;
        var code = statusCode switch
        {
            401 or 403 => "INFLUX_PERMISSION_DENIED",
            404 => "INFLUX_NOT_FOUND",
            429 => "INFLUX_RATE_LIMITED",
            >= 500 => "INFLUX_REMOTE_SERVER_ERROR",
            400 => "INFLUX_BAD_REQUEST",
            _ => "INFLUX_REMOTE_REQUEST_FAILED"
        };
        return new InfluxTransportException(
            code,
            "InfluxDB request failed; see the provider diagnostics for the sanitized error code.",
            statusCode,
            GetRetryAfter(influxException),
            innerException: exception);
    }

    private static bool ContainsUnavailableTransport(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static TimeSpan? GetRetryAfter(InfluxException exception)
    {
        if (exception is HttpException httpException &&
            httpException.RetryAfter is int retryAfterSeconds &&
            retryAfterSeconds > 0)
        {
            return TimeSpan.FromSeconds(retryAfterSeconds);
        }

        return null;
    }

    private static CancellationTokenSource CreateOperationCts(
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCts.CancelAfter(Math.Max(1, timeoutMilliseconds));
        return operationCts;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public sealed record InfluxDbClientSettings(
    string Url,
    string Organization,
    string Bucket,
    string Token,
    int ConnectionTimeoutMilliseconds,
    int WriteTimeoutMilliseconds,
    int QueryTimeoutMilliseconds);
