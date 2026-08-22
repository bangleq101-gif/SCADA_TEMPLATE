using Scada.Core.Configuration;

namespace Scada.Infrastructure.Configuration;

public static class ConfigurationValidator
{
    public static IReadOnlyList<ValidationIssue> CollectIssues(RuntimeOptions options) =>
        RuntimeOptionsValidation.CollectIssues(options);

    public static void Validate(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issues = CollectIssues(options);
        var blockingIssues = issues.Where(issue => issue.IsBlocking).ToArray();
        if (blockingIssues.Length > 0)
        {
            throw new ConfigurationValidationException(blockingIssues);
        }
    }
}
