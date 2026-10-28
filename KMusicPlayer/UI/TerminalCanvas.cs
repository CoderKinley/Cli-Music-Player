namespace KMusicPlayer.UI;

using System.Text;
using KMusicPlayer.UI.Themes;

public static class TerminalCanvas
{
    public static long Version { get; private set; }
    private static StringBuilder? _frame;
    private static int _frameDepth, _frameWidth, _frameHeight;
    public static int Width => _frame is not null ? _frameWidth : Math.Max(1, Console.WindowWidth - 1);
    public static int Height => _frame is not null ? _frameHeight : Math.Max(1, Console.WindowHeight);

    // UI drawing is single-threaded. Nested panels share one viewport snapshot and one write.
    public static IDisposable BeginFrame() => new FrameScope();

    internal static void WriteRaw(string output)
    {
        if (_frame is not null) _frame.Append(output);
        else Console.Write(output);
    }

    private sealed class FrameScope : IDisposable
    {
        private bool _disposed;
        public FrameScope()
        {
            if (_frameDepth == 0)
            {
                _frameWidth = Math.Max(1, Console.WindowWidth - 1);
                _frameHeight = Math.Max(1, Console.WindowHeight);
                _frame = new StringBuilder();
            }
            _frameDepth++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (--_frameDepth != 0) return;
            var output = _frame!.ToString();
            _frame = null;
            if (output.Length > 0) Console.Write(output);
        }
    }

    public static void Clear()
    {
        Version++;
        WriteRaw("\u001b[0m\u001b[2J\u001b[3J\u001b[H");
    }

    public static void DrawBox(int x, int y, int width, int height, string title)
    {
        if (width < 4 || height < 2)
            return;

        var horizontal = new string('─', width - 2);
        WriteAt(x, y, $"┌{horizontal}┐", ConsoleColor.Blue);
        for (var row = 1; row < height - 1; row++)
        {
            WriteAt(x, y + row, "│", ConsoleColor.Blue);
            WriteAt(x + width - 1, y + row, "│", ConsoleColor.Blue);
        }
        WriteAt(x, y + height - 1, $"└{horizontal}┘", ConsoleColor.Blue);
        if (!string.IsNullOrEmpty(title))
            WriteAt(x + 2, y, Fit(title, width - 4), ConsoleColor.Cyan);
    }

    public static void WriteMenuItem(int x, int y, int width, string text, bool selected) =>
        WriteAt(
            x,
            y,
            TerminalText.PadRight($"{(selected ? ">" : " ")} {text}", width),
            selected ? ConsoleColor.Black : ConsoleColor.Gray,
            selected ? ConsoleColor.Cyan : ConsoleColor.Black);

    public static void WriteCentered(int y, string text, ConsoleColor color) =>
        WriteAt(Math.Max(0, (Width - TerminalText.Width(text)) / 2), y, text, color);

    public static void WriteAt(
        int x,
        int y,
        string text,
        ConsoleColor foreground,
        ConsoleColor background = ConsoleColor.Black)
    {
        var width = Width;
        if (y < 0 || y >= Height || x >= width)
            return;

        x = Math.Max(0, x);
        var fitted = Fit(text, Math.Max(0, width - x));
        // Black denotes ordinary/unfilled areas throughout the UI. Let the terminal
        // supply their background, while preserving colored selection/progress fills.
        var backgroundCode = background == ConsoleColor.Black ? 49 : AnsiColor(ThemeManager.Map(background)) + 10;
        WriteRaw($"\u001b[{y + 1};{x + 1}H\u001b[{AnsiColor(ThemeManager.Map(foreground))};{backgroundCode}m{fitted}\u001b[0m");
    }

    private static int AnsiColor(ConsoleColor color)
    {
        // ConsoleColor uses BGR bit order; ANSI uses RGB, with a separate bright range.
        var value = (int)color;
        return 30 + ((value & 1) << 2) + (value & 2) + ((value & 4) >> 2) + ((value & 8) != 0 ? 60 : 0);
    }

    public static string Fit(string text, int width) => TerminalText.Fit(text, width);
}
