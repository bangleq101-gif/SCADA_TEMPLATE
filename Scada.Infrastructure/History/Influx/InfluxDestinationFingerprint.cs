using System.Security.Cryptography;
using System.Text;
using Scada.Core.History;

namespace Scada.Infrastructure.History.Influx;

public static class InfluxDestinationFingerprint
{
    public const int PointSchemaVersion = 1;
    public const string TimestampPrecision = "ns";

    public static string Create(InfluxDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var canonical = string.Join(
            "\n",
            "InfluxDb2",
            NormalizeUrl(options.Url),
            options.Organization,
            options.Bucket,
            options.Measurement,
            PointSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TimestampPrecision);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.Trim();
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{scheme}://{host}{port}{path}";
    }
}
