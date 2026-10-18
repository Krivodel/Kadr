using System.Text;

using Tesseract;

namespace Kadr.Desktop.Services.Ocr;

internal static class OcrTextLayout
{
    internal static string Read(Page page)
    {
        StringBuilder result = new();
        using ResultIterator iterator = page.GetIterator();
        iterator.Begin();

        do
        {
            string? paragraph = iterator.GetText(PageIteratorLevel.Para);

            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                if (result.Length > 0)
                {
                    result.AppendLine();
                }

                result.Append(ReflowParagraph(paragraph));
            }
        }
        while (iterator.Next(PageIteratorLevel.Para));

        return result.ToString().Trim();
    }

    private static string ReflowParagraph(string paragraph)
    {
        string[] lines = paragraph.Trim().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        StringBuilder result = new();

        foreach (string line in lines)
        {
            if (result.Length > 0)
            {
                if (StartsListItem(line))
                {
                    result.AppendLine();
                }
                else
                {
                    result.Append(' ');
                }
            }

            result.Append(line);
        }

        return result.ToString();
    }

    private static bool StartsListItem(string text)
    {
        if (text.StartsWith("• ", StringComparison.Ordinal) || text.StartsWith("- ", StringComparison.Ordinal))
        {
            return true;
        }

        int index = 0;

        while ((index < text.Length) && char.IsDigit(text[index]))
        {
            index++;
        }

        return (index > 0) && (index + 1 < text.Length) && (text[index] is '.' or ')') && char.IsWhiteSpace(text[index + 1]);
    }
}
