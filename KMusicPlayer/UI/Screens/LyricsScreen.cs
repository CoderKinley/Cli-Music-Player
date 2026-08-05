using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.UI.Screens;

public sealed class LyricsScreen
{
    private readonly ILyricsService _lyrics;

    public LyricsScreen(ILyricsService lyrics) => _lyrics = lyrics;

    public async Task ShowAsync(Track track)
    {
        DrawLoading(track);
        var result = await _lyrics.GetAsync(track);

        while (true)
        {
            if (result is null)
            {
                DrawMissing(track);
                var key = Console.ReadKey(intercept: true).Key;
                if (key is ConsoleKey.Q or ConsoleKey.Escape)
                    return;
                if (key != ConsoleKey.E)
                    continue;
            }
            else
            {
                var action = ShowLyrics(track, result);
                if (action == LyricsAction.Close)
                    return;
                if (action == LyricsAction.None)
                    continue;
            }

            var pasted = ReadPastedLyrics(track);
            if (string.IsNullOrWhiteSpace(pasted))
                continue;
            await _lyrics.SaveManualAsync(track, pasted);
            result = new LyricsResult(pasted.Trim(), true);
        }
    }

    private static LyricsAction ShowLyrics(Track track, LyricsResult result)
    {
        var lines = WrapLines(result.Text);
        var offset = 0;
        var lastWidth = -1;
        var lastHeight = -1;
        while (true)
        {
            var pageSize = Math.Max(1, TerminalCanvas.Height - 6);
            offset = Math.Clamp(offset, 0, Math.Max(0, lines.Count - pageSize));
            if (lastWidth != TerminalCanvas.Width || lastHeight != TerminalCanvas.Height)
            {
                lastWidth = TerminalCanvas.Width;
                lastHeight = TerminalCanvas.Height;
                lines = WrapLines(result.Text);
                pageSize = Math.Max(1, lastHeight - 6);
                offset = Math.Clamp(offset, 0, Math.Max(0, lines.Count - pageSize));
                DrawFrame(track, result.IsManual);
            }
            DrawLyricsRows(lines, offset, pageSize);
            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.UpArrow:
                    offset--;
                    break;
                case ConsoleKey.DownArrow:
                    offset++;
                    break;
                case ConsoleKey.PageUp:
                    offset -= pageSize;
                    break;
                case ConsoleKey.PageDown:
                    offset += pageSize;
                    break;
                case ConsoleKey.Home:
                    offset = 0;
                    break;
                case ConsoleKey.End:
                    offset = lines.Count;
                    break;
                case ConsoleKey.E:
                    return LyricsAction.Edit;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                case ConsoleKey.L:
                    return LyricsAction.Close;
            }
        }
    }

    private static void DrawLyricsRows(
        IReadOnlyList<string> lines,
        int offset,
        int pageSize)
    {
        var rowWidth = Math.Max(1, TerminalCanvas.Width - 4);
        for (var index = 0; index < pageSize; index++)
        {
            var text = offset + index < lines.Count ? lines[offset + index] : string.Empty;
            TerminalCanvas.WriteAt(2, 4 + index, text.PadRight(rowWidth), ConsoleColor.Gray);
        }
    }

    private static void DrawFrame(Track track, bool isManual)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, TerminalCanvas.Height, " LYRICS ");
        TerminalCanvas.WriteAt(2, 1,
            TerminalCanvas.Fit($"{track.Title} - {track.Artist}", TerminalCanvas.Width - 4),
            ConsoleColor.Cyan);
        TerminalCanvas.WriteAt(2, 2,
            $"Up/Down/Page: Scroll   E: Edit   L/Q: Back   Source: {(isManual ? "Saved" : "LRCLIB")}",
            ConsoleColor.DarkGray);
    }

    private static IReadOnlyList<string> WrapLines(string lyrics)
    {
        var width = Math.Max(1, TerminalCanvas.Width - 4);
        var output = new List<string>();
        foreach (var line in lyrics.Split('\n'))
        {
            if (line.Length == 0)
            {
                output.Add(string.Empty);
                continue;
            }
            for (var start = 0; start < line.Length; start += width)
                output.Add(line.Substring(start, Math.Min(width, line.Length - start)));
        }
        return output;
    }

    private static string? ReadPastedLyrics(Track track)
    {
        TerminalCanvas.Clear();
        Console.CursorVisible = true;
        try
        {
            Console.WriteLine($"LYRICS: {track.Title} - {track.Artist}");
            Console.WriteLine("Paste/type lyrics below. On a new line enter .save to save, or .cancel to cancel.");
            Console.WriteLine();
            var lines = new List<string>();
            while (true)
            {
                var line = Console.ReadLine();
                if (line is null || line.Equals(".cancel", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (line.Equals(".save", StringComparison.OrdinalIgnoreCase))
                    return string.Join(Environment.NewLine, lines);
                lines.Add(line);
            }
        }
        finally
        {
            Console.CursorVisible = false;
        }
    }

    private static void DrawLoading(Track track)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 6, " LYRICS ");
        TerminalCanvas.WriteAt(2, 2,
            TerminalCanvas.Fit($"Finding lyrics for {track.Title} - {track.Artist}...", TerminalCanvas.Width - 4),
            ConsoleColor.Cyan);
    }

    private static void DrawMissing(Track track)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 7, " LYRICS ");
        TerminalCanvas.WriteAt(2, 2,
            TerminalCanvas.Fit($"No online or saved lyrics found for {track.Title}.", TerminalCanvas.Width - 4),
            ConsoleColor.Yellow);
        TerminalCanvas.WriteAt(2, 4, "E: Paste lyrics manually   Q: Back", ConsoleColor.DarkGray);
    }

    private enum LyricsAction { None, Edit, Close }
}
