using System.Text;

namespace Scada.App.Services;

internal static class DelimitedTextParser
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var rows = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        var rowHasContent = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (inQuotes)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            if (current == '"' && !fieldStarted && field.Length == 0)
            {
                inQuotes = true;
                fieldStarted = true;
                rowHasContent = true;
                continue;
            }

            if (current == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                rowHasContent = true;
                continue;
            }

            if (current == '\r' || current == '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                if (rowHasContent || fields.Count > 1 || fields[0].Length > 0)
                {
                    rows.Add(fields.ToArray());
                }

                fields = [];
                rowHasContent = false;
                continue;
            }

            if (fieldStarted && field.Length == 0)
            {
                throw new FormatException("Unexpected characters after a quoted field.");
            }

            field.Append(current);
            fieldStarted = true;
            rowHasContent = true;
        }

        if (inQuotes)
        {
            throw new FormatException("A quoted field was not closed.");
        }

        if (rowHasContent || fields.Count > 0 || field.Length > 0)
        {
            fields.Add(field.ToString());
            if (rowHasContent || fields.Count > 1 || fields[0].Length > 0)
            {
                rows.Add(fields.ToArray());
            }
        }

        return rows;
    }

    public static string Escape(string value, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains(delimiter) && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
