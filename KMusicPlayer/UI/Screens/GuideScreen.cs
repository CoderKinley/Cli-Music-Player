namespace KMusicPlayer.UI.Screens;

public static class GuideScreen
{
    public static void Show()
    {
        var sections = new[] { "All" }.Concat(GuideCatalog.Entries.Select(entry => entry.Section).Distinct()).ToArray();
        var section = 0;
        var query = "";
        var offset = 0;
        var width = -1;
        var height = -1;
        var dirty = false;
        while (true)
        {
            var entries = GuideCatalog.Find(query, section == 0 ? null : sections[section]);
            var lines = BuildLines(entries, TerminalCanvas.Width - 4);
            offset = Math.Clamp(offset, 0, Math.Max(0, lines.Count - DocumentScreen.PageSize));
            if (dirty && width >= 0)
                DrawContent();
            dirty = false;
            var key = TerminalInput.ReadResponsive(() =>
            {
                DocumentScreen.DrawFrame("KEYBOARD GUIDE");
                DrawContent();
            }, ref width, ref height, handleHelp: false);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: offset--; break;
                case ConsoleKey.DownArrow: offset++; break;
                case ConsoleKey.PageUp: offset -= DocumentScreen.PageSize; break;
                case ConsoleKey.PageDown: offset += DocumentScreen.PageSize; break;
                case ConsoleKey.Home: offset = 0; break;
                case ConsoleKey.End: offset = lines.Count; break;
                case ConsoleKey.LeftArrow:
                    section = (section + sections.Length - 1) % sections.Length;
                    offset = 0;
                    break;
                case ConsoleKey.RightArrow:
                case ConsoleKey.Tab:
                    var direction = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
                    section = (section + sections.Length + direction) % sections.Length;
                    offset = 0;
                    break;
                case ConsoleKey.Oem2 when key.KeyChar != '?':
                    var search = SearchInput.Read(" FIND A SHORTCUT ", "Search keys, actions, or context. Escape keeps the current filter.", "Find: ");
                    if (search is not null)
                    {
                        query = search;
                        section = 0;
                        offset = 0;
                    }
                    width = -1;
                    break;
                case ConsoleKey.Backspace:
                    query = "";
                    offset = 0;
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                case TerminalKeys.Help: return;
            }
            dirty = true;

            void DrawContent()
            {
                TerminalCanvas.WriteAt(2, 2, TerminalText.PadRight(
                    $"{sections[section]} | Left/Right: Category | {entries.Count} shortcuts" +
                    (query.Length == 0 ? "" : $" | Find: {query}"), TerminalCanvas.Width - 4), ConsoleColor.DarkGray);
                DocumentScreen.DrawLines(lines, offset, "Q: Back   /: Find   Backspace: Clear   PgUp/PgDn: Page");
            }
        }
    }

    private static IReadOnlyList<ScreenLine> BuildLines(IReadOnlyList<GuideEntry> entries, int width) 
    {
        if (entries.Count == 0)
            return [new ScreenLine("No shortcuts match. Backspace clears the filter.", ConsoleColor.Yellow)];
        var lines = new List<ScreenLine>();
        foreach (var group in entries.GroupBy(entry => entry.Section))
        {
            lines.Add(new ScreenLine(group.Key.ToUpperInvariant(), ConsoleColor.Cyan));
            foreach (var entry in group)
            {
                lines.AddRange(TerminalText.Wrap($"{entry.Keys}  -  {entry.Action}", width).Select(line => new ScreenLine(line, ConsoleColor.White)));
                lines.AddRange(TerminalText.Wrap(entry.Context, width).Select(line => new ScreenLine(line, ConsoleColor.DarkGray)));
                lines.Add(new ScreenLine(""));
            }
        }
        return lines;
    }
}
