namespace KMusicPlayer.UI;

public static class TerminalInput
{
    public static ConsoleKeyInfo ReadResponsive(
        Action redraw,
        ref int lastWidth,
        ref int lastHeight,
        Func<bool>? idleSignal = null,
        Action? periodicRedraw = null,
        int redrawIntervalMilliseconds = 250,
        bool handleHelp = true)
    {
        var width = Console.WindowWidth;
        var height = Console.WindowHeight;
        var nextRedrawAt = Environment.TickCount64 + redrawIntervalMilliseconds;
        if (width != lastWidth || height != lastHeight)
        {
            (width, height) = WaitForStableWindowSize();
            TerminalCanvas.Clear();
            lastWidth = width;
            lastHeight = height;
            DrawCurrentSize();
        }

        while (true)
        {
            width = Console.WindowWidth;
            height = Console.WindowHeight;

            if (width != lastWidth || height != lastHeight)
            {
                // Let the screen rebuild wrapped text and page sizes before
                // painting the new viewport.
                lastWidth = -1;
                lastHeight = -1;
                return default;
            }

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (!TerminalLayout.Fits(TerminalCanvas.Width, TerminalCanvas.Height))
                {
                    if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                        return key;
                    continue;
                }
                if (handleHelp && (key.Key == TerminalKeys.Help || key.KeyChar == '?'))
                {
                    Screens.GuideScreen.Show();
                    // Return to the caller's loop so data and layout are rebuilt
                    // after help, including any resize that happened inside it.
                    lastWidth = -1;
                    lastHeight = -1;
                    return default;
                }
                return key;
            }

            if (idleSignal?.Invoke() == true)
                return new ConsoleKeyInfo('\0', ConsoleKey.MediaNext, false, false, false);

            if (periodicRedraw is not null && TerminalLayout.Fits(TerminalCanvas.Width, TerminalCanvas.Height) && Environment.TickCount64 >= nextRedrawAt)
            {
                periodicRedraw();
                nextRedrawAt = Environment.TickCount64 + redrawIntervalMilliseconds;
            }

            // Keep key polling inexpensive without limiting animated panels to 20 frames/sec.
            Thread.Sleep(periodicRedraw is null ? 20 : Math.Clamp(redrawIntervalMilliseconds / 4, 1, 10));
        }

        void DrawCurrentSize()
        {
            if (TerminalLayout.Fits(TerminalCanvas.Width, TerminalCanvas.Height))
                redraw();
            else
            {
                TerminalCanvas.WriteAt(0, 0, "Enlarge the terminal to at least 61 columns by 20 rows.", ConsoleColor.Yellow);
                TerminalCanvas.WriteAt(0, Math.Min(2, TerminalCanvas.Height - 1), "Q/Escape: Back", ConsoleColor.DarkGray);
            }
        }
    }

    public static ConsoleKeyInfo ReadWithHelp()
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == TerminalKeys.Help || key.KeyChar == '?')
        {
            Screens.GuideScreen.Show();
            return default;
        }
        return key;
    }

    public static (int Width, int Height) WaitForStableWindowSize()
    {
        var width = Console.WindowWidth;
        var height = Console.WindowHeight;
        var stableChecks = 0;
        var started = Environment.TickCount64;

        while (stableChecks < 2 && Environment.TickCount64 - started < 400)
        {
            Thread.Sleep(50);
            var nextWidth = Console.WindowWidth;
            var nextHeight = Console.WindowHeight;
            if (nextWidth == width && nextHeight == height)
            {
                stableChecks++;
            }
            else
            {
                width = nextWidth;
                height = nextHeight;
                stableChecks = 0;
            }
        }

        return (width, height);
    }
}
