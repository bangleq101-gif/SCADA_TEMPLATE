namespace Scada.Core.Common;

public readonly record struct RuntimeId
{
    public RuntimeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("RuntimeId is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public static RuntimeId Default => new("Runtime01");

    public override string ToString() => Value ?? string.Empty;
}
