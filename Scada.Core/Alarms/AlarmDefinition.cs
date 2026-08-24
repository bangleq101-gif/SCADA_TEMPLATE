namespace Scada.Core.Alarms;

public sealed class AlarmDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }
    public AlarmRuleType RuleType { get; set; }
    public AlarmSeverity Severity { get; set; } = AlarmSeverity.Medium;
    public bool? DigitalExpectedValue { get; set; }
    public double? Threshold { get; set; }
    public double Deadband { get; set; }
    public TimeSpan ActivationDelay { get; set; }
    public bool AcknowledgementRequired { get; set; } = true;
}
