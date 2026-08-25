using System.Text.RegularExpressions;

namespace Scada.Runtime.Health;

public static partial class RuntimeHealthSanitizer
{
    private const int MaximumMessageLength = 256;

    [GeneratedRegex(@"(?i)(password|passwd|pwd|token|secret|api[_-]?key|username)\s*[=:]\s*[^;&\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"(?i)([a-z][a-z0-9+.-]*://)([^/@\s:]+):([^/@\s]+)@", RegexOptions.CultureInvariant)]
    private static partial Regex UriCredentialRegex();

    [GeneratedRegex(@"(?i)\b[A-Z]:\\[^\s;]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    public static string? Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var sanitized = UriCredentialRegex().Replace(message, "$1[redacted]@");
        sanitized = SecretAssignmentRegex().Replace(sanitized, "$1=[redacted]");
        sanitized = WindowsPathRegex().Replace(sanitized, "[path]");
        sanitized = sanitized.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return sanitized.Length <= MaximumMessageLength
            ? sanitized
            : sanitized[..MaximumMessageLength];
    }
}
