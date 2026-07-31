namespace KMusicPlayer.UI.Screens;

using KMusicPlayer.UI.Themes;

public static class SearchInput
{
    public static string? Read()
    {
        var input = new System.Text.StringBuilder();
        Console.CursorVisible = true;
        var lastWidth = -1;
        var lastHeight = -1;
        var inputChanged = true;

        try
        {
            while (true)
            {
                var rawWidth = Console.WindowWidth;
                var rawHeight = Console.WindowHeight;
                var width = Math.Max(1, rawWidth - 1);
                if (rawWidth != lastWidth || rawHeight != lastHeight)
                {
                    (lastWidth, lastHeight) = TerminalInput.WaitForStableWindowSize();
                    width = Math.Max(1, lastWidth - 1);
                    TerminalCanvas.Clear();
                    TerminalCanvas.DrawBox(0, 0, width, 6, " SEARCH ");
                    TerminalCanvas.WriteAt(
                        2, 2,
                        "Type a song, artist, or album. Q or Escape returns Home.",
                        ConsoleColor.DarkGray);
                    TerminalCanvas.WriteAt(2, 3, "Search: ", ConsoleColor.Cyan);
                    inputChanged = true;
                }

                var maxWidth = Math.Max(1, width - 12);
                if (inputChanged)
                {
                    Console.SetCursorPosition(10, 3);
                    Console.ForegroundColor = ThemeManager.Map(ConsoleColor.White);
                    Console.Write(TerminalCanvas.Fit(input.ToString(), maxWidth).PadRight(maxWidth));
                    Console.SetCursorPosition(
                        10 + Math.Min(input.Length, Math.Max(0, maxWidth - 1)),
                        3);
                    Console.ResetColor();
                    inputChanged = false;
                }

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                    case ConsoleKey.Q:
                        return null;
                    case ConsoleKey.Enter:
                        return input.ToString().Trim();
                    case ConsoleKey.Backspace when input.Length > 0:
                        input.Length--;
                        inputChanged = true;
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar) && input.Length < maxWidth)
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
