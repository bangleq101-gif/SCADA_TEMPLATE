using Scada.Core.Tags;

namespace Scada.App.Services;

public static class CsvCodec
{
    public static string Export(IEnumerable<TagDefinition> tags) => TagTableCodec.Export(tags, ',');

    public static IReadOnlyList<TagDefinition> Import(string text) => TagTableCodec.Import(text, ',');
}
