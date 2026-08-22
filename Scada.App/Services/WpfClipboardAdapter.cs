using System.Windows;

namespace Scada.App.Services;

public sealed class WpfClipboardAdapter : IClipboardAdapter
{
    public string? GetText() => Clipboard.ContainsText() ? Clipboard.GetText() : null;

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Clipboard.SetText(text);
    }
}
