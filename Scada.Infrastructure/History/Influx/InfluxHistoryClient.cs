using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;

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
            Timeout = TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds)
        };

        _client = new InfluxDBClient(clientOptions);
        _writeApi = _client.GetWriteApiAsync(null);
        _queryApi = _client.GetQueryApi(null);
    }

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            await _client.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var buckets = await _client.GetBucketsApi()
                .FindBucketsByOrgNameAsync(_settings.Organization, cancellationToken)
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
        try
        {
            await _writeApi.WriteRecordsAsync(
                lineProtocolRecords,
                WritePrecision.Ns,
                bucket,
                organization,
                cancellationToken).ConfigureAwait(false);
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
        try
        {
            var query = new Query
            {
                _Query = flux,
                Type = Query.TypeEnum.Flux
            };
            return await _queryApi.QueryRawAsync(query, organization, cancellationToken).ConfigureAwait(false);
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
        try
        {
            var buckets = await _client.GetBucketsApi()
                .FindBucketsByOrgNameAsync(organization, cancellationToken)
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
        try
        {
            var buckets = await _client.GetBucketsApi()
                .FindBucketsByOrgNameAsync(organization, cancellationToken)
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
            await _client.GetBucketsApi().UpdateBucketAsync(match, cancellationToken).ConfigureAwait(false);
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

    private static InfluxTransportException Translate(Exception exception)
    {
        var statusCode = TryGetStatusCode(exception);
        var code = statusCode switch
        {
            401 or 403 => "INFLUX_PERMISSION_DENIED",
            404 => "INFLUX_NOT_FOUND",
            429 => "INFLUX_RATE_LIMITED",
            >= 500 => "INFLUX_REMOTE_SERVER_ERROR",
            _ => "INFLUX_REMOTE_REQUEST_FAILED"
        };
        return new InfluxTransportException(
            code,
            "InfluxDB request failed; see the provider diagnostics for the sanitized error code.",
            statusCode,
            innerException: exception);
    }

    private static int? TryGetStatusCode(Exception exception)
    {
        var property = exception.GetType().GetProperty("ErrorCode");
        if (property?.GetValue(exception) is string errorCode && int.TryParse(errorCode, out var parsed))
        {
            return parsed;
        }

        return null;
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
    int TimeoutMilliseconds);
