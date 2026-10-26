using KMusicPlayer.Domain;

namespace KMusicPlayer.UI.Screens;

public sealed record ScreenLine(string Text, ConsoleColor Color = ConsoleColor.Gray);

public static class DocumentScreen
{
    public static int PageSize => Math.Max(1, TerminalCanvas.Height - 7);

    public static void DrawFrame(string title) =>
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, TerminalCanvas.Height, $" {title} ");

    public static void DrawLines(IReadOnlyList<ScreenLine> lines, int offset, string footer)
    {
        var width = TerminalCanvas.Width - 4;
        for (var row = 0; row < PageSize; row++)
        {
            var line = offset + row < lines.Count ? lines[offset + row] : new ScreenLine("");
            TerminalCanvas.WriteAt(2, 4 + row, TerminalText.PadRight(line.Text, width), line.Color);
        }
        TerminalCanvas.WriteAt(2, TerminalCanvas.Height - 3,
            TerminalText.PadRight($"{Math.Min(offset + 1, lines.Count)}-{Math.Min(offset + PageSize, lines.Count)} of {lines.Count} lines", width),
            ConsoleColor.DarkGray);
        TerminalCanvas.WriteAt(2, TerminalCanvas.Height - 2, TerminalText.PadRight(footer, width), ConsoleColor.Cyan);
    }

    public static void ShowTrack(Track track)
    {
        var offset = 0;
        var width = -1;
        var height = -1;
        var dirty = false;
        while (true)
        {
            var lines = new List<ScreenLine>();
            Add("TITLE", track.Title);
            Add("ARTIST / AUTHOR", track.Artist);
            Add("DURATION", track.DurationText);
            Add("SOURCE", track.IsLocal ? $"Local file: {track.Id[6..]}" : $"https://www.youtube.com/watch?v={track.Id}");
            offset = Math.Clamp(offset, 0, Math.Max(0, lines.Count - PageSize));
            if (dirty && width >= 0)
                DrawLines(lines, offset, "Q/Esc: Back   Up/Down: Scroll   PgUp/PgDn: Page   F1: Guide");
            dirty = false;
            var currentOffset = offset;
            var key = TerminalInput.ReadResponsive(() =>
            {
                DrawFrame("TRACK DETAILS");
                TerminalCanvas.WriteAt(2, 2, "Full song information. Scroll to read long names.", ConsoleColor.DarkGray);
                DrawLines(lines, currentOffset, "Q/Esc: Back   Up/Down: Scroll   PgUp/PgDn: Page   F1: Guide");
            }, ref width, ref height);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: offset--; break;
                case ConsoleKey.DownArrow: offset++; break;
                case ConsoleKey.PageUp: offset -= PageSize; break;
                case ConsoleKey.PageDown: offset += PageSize; break;
                case ConsoleKey.Home: offset = 0; break;
                case ConsoleKey.End: offset = lines.Count; break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                case TerminalKeys.TrackDetails: return;
            }
            dirty = true;

            void Add(string heading, string text)
            {
                lines.Add(new ScreenLine(heading, ConsoleColor.Cyan));
                lines.AddRange(TerminalText.Wrap(text, TerminalCanvas.Width - 4).Select(line => new ScreenLine(line)));
                lines.Add(new ScreenLine(""));
            }
        }
    }
}
