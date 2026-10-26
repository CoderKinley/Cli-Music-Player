using System.Globalization;
using System.Text;

namespace KMusicPlayer.UI.Screens;

public static class SearchInput
{
    public static string? Read() => Read(
        " SEARCH ", "Type a song, artist, or album. Enter: Search   Escape: Back   F1: Guide", "Search: ");

    public static string? Read(string title, string instruction, string label)
    {
        var input = new StringBuilder();
        var lastWidth = -1;
        var lastHeight = -1;
        var inputChanged = true;
        Console.CursorVisible = false;

        try
        {
            while (true)
            {
                if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
                {
                    Console.CursorVisible = false;
                    (lastWidth, lastHeight) = TerminalInput.WaitForStableWindowSize();
                    TerminalCanvas.Clear();
                    TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, Math.Min(6, lastHeight), title);
                    TerminalCanvas.WriteAt(2, 2,
                        TerminalCanvas.Fit(instruction, TerminalCanvas.Width - 4), ConsoleColor.DarkGray);
                    TerminalCanvas.WriteAt(2, 3,
                        TerminalCanvas.Fit(label, TerminalCanvas.Width - 4), ConsoleColor.Cyan);
                    inputChanged = true;
                }

                if (inputChanged)
                {
                    // Hide the caret while writing the field so it never jumps
                    // between the end of the cleared row and the editing position.
                    Console.CursorVisible = false;
                    var x = Math.Min(2 + TerminalText.Width(label), Math.Max(0, TerminalCanvas.Width - 2));
                    var y = Math.Min(3, Math.Max(0, lastHeight - 1));
                    var available = Math.Max(1, TerminalCanvas.Width - x - 2);
                    var visible = TerminalText.Tail(input.ToString(), available - 1);
                    TerminalCanvas.WriteAt(x, y, TerminalText.PadRight(visible, available), ConsoleColor.White);
                    Console.SetCursorPosition(x + TerminalText.Width(visible), y);
                    Console.CursorVisible = true;
                    inputChanged = false;
                }

                // Idle polling must not repaint the field or move the caret.
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case TerminalKeys.Help:
                        Console.CursorVisible = false;
                        GuideScreen.Show();
                        lastWidth = -1;
                        inputChanged = true;
                        break;
                    case ConsoleKey.Escape:
                        return null;
                    case ConsoleKey.Enter:
                        return input.ToString().Trim();
                    case ConsoleKey.Backspace when input.Length > 0:
                        var starts = StringInfo.ParseCombiningCharacters(input.ToString());
                        input.Length = starts[^1];
                        inputChanged = true;
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            input.Append(key.KeyChar);
                            inputChanged = true;
                        }
                        break;
                }
            }
        }
        finally
        {
            Console.CursorVisible = false;
        }
    }
}
