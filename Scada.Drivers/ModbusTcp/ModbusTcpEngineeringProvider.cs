using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;

namespace Scada.Drivers.ModbusTcp;

public sealed class ModbusTcpEngineeringProvider : IDriverEngineeringProvider, IDriverTagEngineeringProvider
{
    public string DriverType => "ModbusTcp";

    public IReadOnlyList<DriverOptionDefinition> OptionDefinitions { get; } =
    [
        new(ModbusTcpOptions.HostKey, "Host", DriverOptionValueType.String, string.Empty,
            IsRequired: true, Description: "DNS name or IP address of the Modbus TCP server."),
        new(ModbusTcpOptions.PortKey, "Port", DriverOptionValueType.Integer, "502"),
        new(ModbusTcpOptions.UnitIdKey, "Unit identifier", DriverOptionValueType.Integer, "1"),
        new(ModbusTcpOptions.ByteOrderKey, "Register byte order", DriverOptionValueType.String,
            nameof(ModbusRegisterByteOrder.BigEndian), IsAdvanced: true),
        new(ModbusTcpOptions.WordOrderKey, "Register word order", DriverOptionValueType.String,
            nameof(ModbusRegisterWordOrder.HighToLow), IsAdvanced: true)
    ];

    public IReadOnlyList<ValidationIssue> Validate(DeviceDefinition device)
    {
        ModbusTcpOptions.TryParse(device, out _, out var issues);
        return issues;
    }

    public IReadOnlyList<ValidationIssue> ValidateTag(DeviceDefinition device, TagDefinition tag)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(tag);
        if (!ModbusAddress.TryParse(tag.Address, out var address, out var error))
        {
            return [new ValidationIssue(
                "MODBUS_TCP_TAG_ADDRESS_INVALID",
                ValidationSeverity.Error,
                "Tag",
                tag.Id,
                nameof(tag.Address),
                error)];
        }

        var sourceType = tag.GetEffectiveSourceDataType();
        if (address!.DataType != sourceType)
        {
            return [new ValidationIssue(
                "MODBUS_TCP_TAG_TYPE_MISMATCH",
                ValidationSeverity.Error,
                "Tag",
                tag.Id,
                nameof(tag.SourceDataType),
                $"Address '{tag.Address}' produces '{address.DataType}', but the tag source type is '{sourceType}'.")];
        }

        return [];
    }

    public Task<IReadOnlyList<AddressBrowseCandidate>> BrowseAddressesAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AddressBrowseCandidate>>([]);
    }
}
