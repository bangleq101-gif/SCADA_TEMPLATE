using Scada.Core.Configuration;

namespace Scada.Infrastructure.Configuration;

public sealed class ConfigurationValidationException : InvalidOperationException
{
    public ConfigurationValidationException(IReadOnlyList<ValidationIssue> issues)
        : base(string.Join(Environment.NewLine, issues.Select(issue => issue.Message)))
    {
        Issues = issues;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }
}
