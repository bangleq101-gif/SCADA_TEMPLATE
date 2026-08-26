using Scada.Core.Tags;

namespace Scada.Core.Drivers;

public sealed record AddressBrowseCandidate(
    string Address,
    TagDataType DataType,
    string Description);
