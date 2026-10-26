using KMusicPlayer.UI.Themes;

namespace KMusicPlayer.UI;

public sealed record PlaybackPanelFrame(
    int Top, int Width, long CanvasVersion, TerminalTheme Theme,
    string Heading, string Title, string Time, bool IsPlaying, int Filled);

/// <summary>Paints only changed regions; clearing the canvas invalidates the cached frame.</summary>
public sealed class PlaybackPanel
{
    private readonly Action<int, int, string, ConsoleColor, ConsoleColor> _write;
    private PlaybackPanelFrame? _previous;

    public PlaybackPanel(Action<int, int, string, ConsoleColor, ConsoleColor>? write = null) =>
        _write = write ?? TerminalCanvas.WriteAt;

    public void Draw(PlaybackPanelFrame frame)
    {
        if (frame.Width < 10)
            return;

        var previous = _previous;
        var repaint = previous is null || previous.Top != frame.Top || previous.Width != frame.Width
            || previous.CanvasVersion != frame.CanvasVersion || previous.Theme != frame.Theme;
        var interior = frame.Width - 2;
        var barWidth = frame.Width - 4;
        frame = frame with { Filled = Math.Clamp(frame.Filled, 0, barWidth) };

        if (repaint || previous!.Heading != frame.Heading)
        {
            // Compose the border around the heading instead of drawing through its text first.
            var heading = TerminalText.Fit(frame.Heading, frame.Width - 4);
            Write(0, 0, "┌─", ConsoleColor.Blue);
            Write(2, 0, heading, ConsoleColor.Cyan);
            var end = 2 + TerminalText.Width(heading);
            Write(end, 0, new string('─', frame.Width - end - 1) + "┐", ConsoleColor.Blue);
        }
        if (repaint)
        {
            for (var row = 1; row <= 2; row++)
            {
                Write(0, row, "│ ", ConsoleColor.Blue);
                Write(frame.Width - 2, row, " │", ConsoleColor.Blue);
            }
            Write(0, 3, "└" + new string('─', interior) + "┘", ConsoleColor.Blue);
        }

        var timeWidth = TerminalText.Width(frame.Time);
        var titleWidth = Math.Max(0, frame.Width - timeWidth - 6);
        var title = TerminalText.PadRight($"> {frame.Title}", titleWidth) + "  ";
        var titleColor = frame.IsPlaying ? ConsoleColor.Cyan : ConsoleColor.DarkGray;
        if (repaint || previous!.IsPlaying != frame.IsPlaying
            || TerminalText.Width(previous.Time) != timeWidth
            || TerminalText.PadRight($"> {previous.Title}", titleWidth) + "  " != title)
            Write(2, 1, title, titleColor);
        if (repaint || previous!.Time != frame.Time)
            Write(frame.Width - timeWidth - 2, 1, frame.Time, ConsoleColor.Gray);

        if (repaint)
        {
            if (frame.Filled > 0)
                Write(2, 2, new string(' ', frame.Filled), ConsoleColor.Blue, ConsoleColor.Blue);
            if (frame.Filled < barWidth)
                Write(2 + frame.Filled, 2, new string('░', barWidth - frame.Filled), ConsoleColor.DarkGray);
        }
        else if (previous!.Filled != frame.Filled)
        {
            var forward = frame.Filled > previous.Filled;
            Write(2 + Math.Min(previous.Filled, frame.Filled), 2,
                new string(forward ? ' ' : '░', Math.Abs(frame.Filled - previous.Filled)),
                forward ? ConsoleColor.Blue : ConsoleColor.DarkGray,
                forward ? ConsoleColor.Blue : ConsoleColor.Black);
        }
        _previous = frame;

        void Write(int x, int row, string text, ConsoleColor foreground, ConsoleColor background = ConsoleColor.Black) =>
            _write(x, frame.Top + row, text, foreground, background);
    }
}
