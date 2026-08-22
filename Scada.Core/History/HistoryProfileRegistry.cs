namespace Scada.Core.History;

public sealed class HistoryProfileRegistry
{
    private readonly IReadOnlyDictionary<string, HistoryProfileDefinition> _profiles;

    public HistoryProfileRegistry(IEnumerable<HistoryProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var map = new Dictionary<string, HistoryProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            {
                map[profile.Name] = profile;
            }
        }

        _profiles = map;
    }

    public IReadOnlyDictionary<string, HistoryProfileDefinition> Profiles => _profiles;

    public bool TryGet(string name, out HistoryProfileDefinition? profile) =>
        _profiles.TryGetValue(name, out profile);
}
