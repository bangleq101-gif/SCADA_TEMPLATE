using Scada.Core.Tags;

namespace Scada.Core.Drivers;

public sealed record DriverReadRequest(string TagId, string Address, TagDataType DataType);
