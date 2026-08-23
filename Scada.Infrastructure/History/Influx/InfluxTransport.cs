namespace Scada.Infrastructure.History.Influx;

public sealed class InfluxTransportException : Exception
{
    public InfluxTransportException(
        string code,
        string message,
        int? statusCode = null,
        TimeSpan? retryAfter = null,
        bool pointSpecific = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        IsPointSpecific = pointSpecific;
    }

    public string Code { get; }

    public int? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    public bool IsPointSpecific { get; }
}

public sealed record InfluxRetentionInfo(long? EverySeconds, string? BucketId);

public interface IInfluxTransport : IAsyncDisposable
{
    Task ProbeAsync(CancellationToken cancellationToken);

    Task WriteLinesAsync(
        IReadOnlyList<string> lineProtocolRecords,
        string bucket,
        string organization,
        CancellationToken cancellationToken);

    Task<string> QueryRawAsync(
        string flux,
        string organization,
        CancellationToken cancellationToken);

    Task<InfluxRetentionInfo> ReadRetentionAsync(
        string organization,
        string bucket,
        CancellationToken cancellationToken);

    Task ApplyRetentionAsync(
        string organization,
        string bucket,
        long retentionSeconds,
        CancellationToken cancellationToken);
}
