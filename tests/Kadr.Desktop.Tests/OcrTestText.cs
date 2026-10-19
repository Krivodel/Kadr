namespace Kadr.Desktop.Tests;

internal static class OcrTestText
{
    internal const string CyrillicChart = "ШБ МНК ЫМБШ БЫНКМ ИНШМК НШЫИКБ ШИНБКЫ КНШМЫБИ БКШМИЫН НКИБМШЫБ ШИНКМИЫБ ИМШЫНБМК";

    internal static int CountCharacterErrors(string expected, string actual)
    {
        string reference = new(expected.Where(character => !char.IsWhiteSpace(character)).ToArray());
        string text = new(actual.Where(character => !char.IsWhiteSpace(character)).ToArray());
        int[] previous = Enumerable.Range(0, text.Length + 1).ToArray();
        for (int row = 1; row <= reference.Length; row++)
        {
            int[] next = new int[text.Length + 1];
            next[0] = row;
            for (int column = 1; column <= text.Length; column++)
            {
                next[column] = Math.Min(Math.Min(previous[column] + 1, next[column - 1] + 1),
                    previous[column - 1] + (reference[row - 1] == text[column - 1] ? 0 : 1));
            }
            previous = next;
        }

        return previous[^1];
    }
}
