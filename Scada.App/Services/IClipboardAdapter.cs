namespace Scada.App.Services;

public interface IClipboardAdapter
{
    string? GetText();

    void SetText(string text);
}
