using Scada.Core.Tags;

namespace Scada.Runtime.Tags;

public interface ITagCache
{
    bool TryGet(string tagId, out TagValue? value);

    IDisposable Subscribe(string tagId, Action<TagValue> callback);
}
