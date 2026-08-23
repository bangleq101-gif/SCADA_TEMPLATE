namespace Scada.Infrastructure.History.Influx;

public static class InfluxSecretResolver
{
    public static bool TryResolve(
        string? tokenReference,
        out string? token,
        out string errorCode,
        out string errorMessage)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(tokenReference) ||
            !tokenReference.StartsWith("env:", StringComparison.Ordinal))
        {
            errorCode = "INFLUX_TOKEN_REFERENCE_INVALID";
            errorMessage = "InfluxDB token reference must use the env:<VARIABLE_NAME> format.";
            return false;
        }

        var variableName = tokenReference[4..];
        if (variableName.Length == 0 ||
            !IsValidVariableName(variableName))
        {
            errorCode = "INFLUX_TOKEN_REFERENCE_INVALID";
            errorMessage = "InfluxDB token reference contains an invalid environment variable name.";
            return false;
        }

        token = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = null;
            errorCode = "INFLUX_TOKEN_REQUIRED";
            errorMessage = "InfluxDB token environment variable is not configured.";
            return false;
        }

        errorCode = string.Empty;
        errorMessage = string.Empty;
        return true;
    }

    private static bool IsValidVariableName(string value)
    {
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }
}
