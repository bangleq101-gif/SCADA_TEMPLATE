using Scada.Core.History;

namespace Scada.App.Controls;

public static class HistorySettingsViewModes
{
    public static Array All { get; } = Enum.GetValues<HistoryMode>();
}
