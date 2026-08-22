using Scada.Core.Tags;

namespace Scada.App.Services;

public static class TagClipboardCodec
{
    public static string Export(IEnumerable<TagDefinition> tags) => TagTableCodec.Export(tags, '\t');

    public static IReadOnlyList<TagDefinition> Import(string text) => TagTableCodec.Import(text, '\t');
}
