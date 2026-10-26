using System.Globalization;
using System.Text;

namespace KMusicPlayer.UI;

/// <summary>Measures and clips text in terminal cells without splitting grapheme clusters.</summary>
public static class TerminalText
{
    public static int Width(string text) => Elements(text).Sum(element => CellWidth(element));

    public static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        var elements = Elements(text).ToList();
        if (elements.Sum(CellWidth) <= width)
            return string.Concat(elements);

        var suffix = width > 3 ? "..." : string.Empty;
        var remaining = width - suffix.Length;
        var result = new StringBuilder();
        foreach (var element in elements)
        {
            var cells = CellWidth(element);
            if (cells > remaining)
                break;
            result.Append(element);
            remaining -= cells;
        }
        return result.Append(suffix).ToString();
    }

    public static string PadRight(string text, int width)
    {
        var fitted = Fit(text, width);
        return fitted + new string(' ', Math.Max(0, width - Width(fitted)));
    }

    public static string Tail(string text, int width)
    {
        var elements = Elements(text).ToList();
        var start = elements.Count;
        var cells = 0;
        while (start > 0 && cells + CellWidth(elements[start - 1]) <= width)
            cells += CellWidth(elements[--start]);
        return string.Concat(elements.Skip(start));
    }

    public static string TrackLabel(string title, string artist, int width)
    {
        if (string.IsNullOrWhiteSpace(artist) || width < 12)
            return Fit(title, width);

        const string separator = " - ";
        var available = width - separator.Length;
        var artistWidth = Math.Min(Width(artist), Math.Max(4, available / 3));
        var titleWidth = Math.Min(Width(title), available - artistWidth);
        artistWidth = Math.Min(Width(artist), available - titleWidth);
        return Fit(title, titleWidth) + separator + Fit(artist, artistWidth);
    }

    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        width = Math.Max(1, width);
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = new StringBuilder();
            var cells = 0;
            foreach (var element in Elements(paragraph))
            {
                var nextWidth = CellWidth(element);
                if (cells > 0 && cells + nextWidth > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    cells = 0;
                }
                // A double-width grapheme cannot fit in a one-cell viewport.
                if (nextWidth > width)
                    continue;
                line.Append(element);
                cells += nextWidth;
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    private static IEnumerable<string> Elements(string text)
    {
        // Metadata must stay on one row and must never execute terminal controls.
        var clean = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            clean.Append(Rune.IsControl(rune) || rune.Value is 0x2028 or 0x2029 ? " " : rune.ToString());

        var enumerator = StringInfo.GetTextElementEnumerator(clean.ToString());
        while (enumerator.MoveNext())
            yield return enumerator.GetTextElement();
    }

    private static int CellWidth(string element)
    {
        var width = 0;
        var emojiPresentation = false;
        foreach (var rune in element.EnumerateRunes())
        {
            var value = rune.Value;
            emojiPresentation |= value is 0xFE0F or 0x20E3;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or
                UnicodeCategory.Format)
                continue;

            // Wide East Asian characters and emoji occupy two cells. A joined
            // emoji or flag is one grapheme, so use the widest rune in the cluster.
            var wide = value is >= 0x1100 and <= 0x115F or 0x2329 or 0x232A or
                >= 0x231A and <= 0x231B or >= 0x23E9 and <= 0x23EC or 0x23F0 or 0x23F3 or
                >= 0x25FD and <= 0x25FE or >= 0x2614 and <= 0x2615 or
                >= 0x2648 and <= 0x2653 or 0x267F or 0x2693 or 0x26A1 or
                >= 0x26AA and <= 0x26AB or >= 0x26BD and <= 0x26BE or
                >= 0x26C4 and <= 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or
                >= 0x26F2 and <= 0x26F3 or 0x26F5 or 0x26FA or 0x26FD or 0x2705 or
                >= 0x270A and <= 0x270B or 0x2728 or 0x274C or 0x274E or
                >= 0x2753 and <= 0x2755 or 0x2757 or >= 0x2795 and <= 0x2797 or
                0x27B0 or 0x27BF or >= 0x2B1B and <= 0x2B1C or 0x2B50 or 0x2B55 or
                >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or
                >= 0xF900 and <= 0xFAFF or >= 0xFE10 and <= 0xFE19 or
                >= 0xFE30 and <= 0xFE6F or >= 0xFF01 and <= 0xFF60 or
                >= 0xFFE0 and <= 0xFFE6 or >= 0x1F000 and <= 0x1FAFF or
                >= 0x20000 and <= 0x3FFFD;
            width = Math.Max(width, wide ? 2 : 1);
        }
        var displayWidth = emojiPresentation ? Math.Max(2, width) : width;
        // Win32 console buffers can allocate a cell per UTF-16 code unit for
        // joined emoji and combining sequences, even when a terminal draws one
        // glyph. Reserve that space so cursor-addressed rows cannot overlap.
        return OperatingSystem.IsWindows() ? Math.Max(displayWidth, element.Length) : displayWidth;
    }
}
