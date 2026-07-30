namespace KMusicPlayer.UI;

using KMusicPlayer.UI.Themes;

public static class TerminalCanvas
{
    public static int Width => Math.Max(1, Console.WindowWidth - 1);
    public static int Height => Math.Max(1, Console.WindowHeight);

    public static void Clear()
    {
        Console.ResetColor();
        Console.Write("\u001b[2J\u001b[3J\u001b[H");
        try
        {
            Console.SetCursorPosition(0, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
        }
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
            Fit($"{(selected ? ">" : " ")} {text}", width).PadRight(width),
            selected ? ConsoleColor.Black : ConsoleColor.Gray,
            selected ? ConsoleColor.Cyan : ConsoleColor.Black);

    public static void WriteCentered(int y, string text, ConsoleColor color) =>
        WriteAt(Math.Max(0, (Width - text.Length) / 2), y, text, color);

    public static void WriteAt(
        int x,
        int y,
        string text,
        ConsoleColor foreground,
        ConsoleColor background = ConsoleColor.Black)
    {
        if (y < 0 || y >= Console.WindowHeight || x >= Console.WindowWidth)
            return;

        x = Math.Max(0, x);
        Console.SetCursorPosition(x, y);
        Console.ForegroundColor = ThemeManager.Map(foreground);
        Console.BackgroundColor = ThemeManager.Map(background);
        Console.Write(Fit(text, Console.WindowWidth - x));
        Console.ResetColor();
    }

    public static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        if (text.Length <= width)
            return text;
        return width <= 3 ? text[..width] : string.Concat(text.AsSpan(0, width - 3), "...");
    }
}
