using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Scada.Core.Alarms;

public static class AlarmDefinitionFingerprint
{
    public static string Create(AlarmDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var material = string.Join('|',
            definition.Id,
            definition.TagId,
            ((int)definition.RuleType).ToString(CultureInfo.InvariantCulture),
            definition.DigitalExpectedValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            definition.Threshold?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            definition.Deadband.ToString("R", CultureInfo.InvariantCulture),
            definition.ActivationDelay.Ticks.ToString(CultureInfo.InvariantCulture),
            definition.AcknowledgementRequired.ToString(CultureInfo.InvariantCulture),
            ((int)definition.Severity).ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
