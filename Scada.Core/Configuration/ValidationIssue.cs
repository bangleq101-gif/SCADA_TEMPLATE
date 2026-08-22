namespace Scada.Core.Configuration;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string? ObjectType,
    string? ObjectId,
    string? PropertyName,
    string Message)
{
    public bool IsBlocking => Severity == ValidationSeverity.Error;
}
