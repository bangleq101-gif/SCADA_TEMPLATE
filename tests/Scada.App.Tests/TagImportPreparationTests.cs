using Scada.App.Services;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class TagImportPreparationTests
{
    [Fact]
    public void CsvWithoutIdHeaderProducesGeneratedStableIdDuringPreparation()
    {
        var imported = CsvCodec.Import("Name,DeviceId,Address\nImported,SIM01,A9\n");

        var first = TagImportPreparer.Prepare(imported, []);
        var second = TagImportPreparer.Prepare(imported, []);

        var firstCandidate = Assert.Single(first.Candidates);
        var secondCandidate = Assert.Single(second.Candidates);
        Assert.True(firstCandidate.IdGenerated);
        Assert.False(string.IsNullOrWhiteSpace(firstCandidate.Definition.Id));
        Assert.Equal(firstCandidate.Definition.Id, secondCandidate.Definition.Id);
        Assert.Empty(first.Conflicts);
    }

    [Fact]
    public void SuppliedUniqueIdIsPreserved()
    {
        var imported = new TagDefinition
        {
            Id = "T2",
            Name = "Imported",
            DeviceId = "SIM01",
            Address = "A9"
        };

        var preparation = TagImportPreparer.Prepare([imported], Existing("T1", "Existing"));

        var candidate = Assert.Single(preparation.Candidates);
        Assert.Equal("T2", candidate.Definition.Id);
        Assert.False(candidate.IdGenerated);
        Assert.Empty(preparation.Conflicts);
    }

    [Fact]
    public void SuppliedConflictingIdIsReportedAndNotRegenerated()
    {
        var imported = new TagDefinition
        {
            Id = "T1",
            Name = "Imported",
            DeviceId = "SIM01",
            Address = "A9"
        };

        var preparation = TagImportPreparer.Prepare([imported], Existing("T1", "Existing"));

        var candidate = Assert.Single(preparation.Candidates);
        Assert.Equal("T1", candidate.Definition.Id);
        Assert.Contains(preparation.Conflicts, conflict => conflict.Kind == TagImportConflictKind.Id);
        Assert.Empty(preparation.NonConflictingCandidates);
    }

    [Fact]
    public void ConflictingNameIsReportedWithoutSilentSuffix()
    {
        var imported = new TagDefinition
        {
            Id = "T2",
            Name = "Existing",
            DeviceId = "SIM01",
            Address = "A9"
        };

        var preparation = TagImportPreparer.Prepare([imported], Existing("T1", "Existing"));

        var candidate = Assert.Single(preparation.Candidates);
        Assert.Equal("Existing", candidate.Definition.Name);
        Assert.Contains(preparation.Conflicts, conflict => conflict.Kind == TagImportConflictKind.Name);
    }

    private static IReadOnlyList<TagDefinition> Existing(string id, string name) =>
    [
        new TagDefinition
        {
            Id = id,
            Name = name,
            DeviceId = "SIM01",
            Address = "A1"
        }
    ];
}
