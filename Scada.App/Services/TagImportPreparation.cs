using System.Security.Cryptography;
using System.Text;
using Scada.Core.Tags;

namespace Scada.App.Services;

public enum TagImportDecision
{
    Cancel,
    ApplyAll,
    AppendNonConflicting
}

public enum TagImportConflictKind
{
    Id,
    Name
}

public sealed record PreparedTagImport(
    TagDefinition Definition,
    int SourceRow,
    bool IdGenerated);

public sealed record TagImportConflict(
    int SourceRow,
    TagImportConflictKind Kind,
    string Value,
    string Message);

public sealed class TagImportPreparation
{
    private readonly IReadOnlyList<PreparedTagImport> _nonConflictingCandidates;

    public TagImportPreparation(
        IReadOnlyList<PreparedTagImport> candidates,
        IReadOnlyList<TagImportConflict> conflicts)
    {
        Candidates = candidates;
        Conflicts = conflicts;
        var conflictedSourceRows = conflicts
            .Select(conflict => conflict.SourceRow)
            .ToHashSet();
        _nonConflictingCandidates = candidates
            .Where(candidate => !conflictedSourceRows.Contains(candidate.SourceRow))
            .ToArray();
    }

    public IReadOnlyList<PreparedTagImport> Candidates { get; }

    public IReadOnlyList<TagImportConflict> Conflicts { get; }

    public bool HasConflicts => Conflicts.Count > 0;

    public IReadOnlyList<PreparedTagImport> NonConflictingCandidates => _nonConflictingCandidates;
}

public interface ITagImportDecisionService
{
    TagImportDecision Decide(TagImportPreparation preparation, string operation);
}

public static class TagImportPreparer
{
    public static TagImportPreparation Prepare(
        IEnumerable<TagDefinition> imported,
        IEnumerable<TagDefinition> existing)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(existing);

        var existingTags = existing.ToArray();
        var importedTags = imported.ToArray();
        var usedIds = existingTags
            .Select(tag => tag.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in importedTags.Where(tag => !string.IsNullOrWhiteSpace(tag.Id)))
        {
            usedIds.Add(tag.Id);
        }

        var candidates = new List<PreparedTagImport>(importedTags.Length);
        for (var index = 0; index < importedTags.Length; index++)
        {
            var tag = CloneTag(importedTags[index]);
            var idGenerated = string.IsNullOrWhiteSpace(tag.Id);
            if (idGenerated)
            {
                tag.Id = CreateStableId(tag, index + 2, usedIds);
            }

            candidates.Add(new PreparedTagImport(tag, index + 2, idGenerated));
        }

        var conflicts = new List<TagImportConflict>();
        var existingIds = existingTags
            .Select(tag => tag.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingNames = existingTags
            .Select(tag => tag.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateIds = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Definition.Id))
            .GroupBy(candidate => candidate.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var candidateNames = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Definition.Name))
            .GroupBy(candidate => candidate.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var tag = candidate.Definition;
            if (!candidate.IdGenerated && existingIds.Contains(tag.Id))
            {
                conflicts.Add(new TagImportConflict(
                    candidate.SourceRow,
                    TagImportConflictKind.Id,
                    tag.Id,
                    $"Id '{tag.Id}' already exists in the project."));
            }

            if (candidateIds.ContainsKey(tag.Id))
            {
                conflicts.Add(new TagImportConflict(
                    candidate.SourceRow,
                    TagImportConflictKind.Id,
                    tag.Id,
                    $"Id '{tag.Id}' is duplicated in the import."));
            }

            if (!string.IsNullOrWhiteSpace(tag.Name) && existingNames.Contains(tag.Name))
            {
                conflicts.Add(new TagImportConflict(
                    candidate.SourceRow,
                    TagImportConflictKind.Name,
                    tag.Name,
                    $"Name '{tag.Name}' already exists in the project."));
            }

            if (!string.IsNullOrWhiteSpace(tag.Name) && candidateNames.ContainsKey(tag.Name))
            {
                conflicts.Add(new TagImportConflict(
                    candidate.SourceRow,
                    TagImportConflictKind.Name,
                    tag.Name,
                    $"Name '{tag.Name}' is duplicated in the import."));
            }
        }

        return new TagImportPreparation(candidates, conflicts);
    }

    private static string CreateStableId(TagDefinition tag, int sourceRow, ISet<string> usedIds)
    {
        var seed = string.Join('\u001f', sourceRow, tag.Name, tag.Description, tag.DeviceId, tag.Address, tag.DataType, tag.ScanGroup);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var baseId = $"TAG_IMPORT_{Convert.ToHexString(hash.AsSpan(0, 8))}";
        var candidate = baseId;
        var suffix = 2;
        while (!usedIds.Add(candidate))
        {
            candidate = $"{baseId}_{suffix++}";
        }

        return candidate;
    }

    private static TagDefinition CloneTag(TagDefinition source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Description = source.Description,
        DeviceId = source.DeviceId,
        Address = source.Address,
        DataType = source.DataType,
        Enabled = source.Enabled,
        ScanGroup = source.ScanGroup,
        AccessMode = source.AccessMode,
        Min = source.Min,
        Max = source.Max,
        Unit = source.Unit,
        HistoryEnabled = source.HistoryEnabled,
        HistoryProfile = source.HistoryProfile,
        MqttPublishEnabled = source.MqttPublishEnabled,
        MqttProfile = source.MqttProfile,
        MqttTopicOverride = source.MqttTopicOverride
    };
}
