namespace KMusicPlayer.UI;

public static class TerminalInput
{
    public static ConsoleKeyInfo ReadResponsive(
        Action redraw,
        ref int lastWidth,
        ref int lastHeight,
        Func<bool>? idleSignal = null,
        Action? periodicRedraw = null,
        int redrawIntervalMilliseconds = 250)
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
            redraw();
        }

        while (true)
        {
            width = Console.WindowWidth;
            height = Console.WindowHeight;

            if (width != lastWidth || height != lastHeight)
            {
                (width, height) = WaitForStableWindowSize();
                TerminalCanvas.Clear();
                redraw();
                lastWidth = width;
                lastHeight = height;
            }

            if (Console.KeyAvailable)
                return Console.ReadKey(intercept: true);

            if (idleSignal?.Invoke() == true)
                return new ConsoleKeyInfo('\0', ConsoleKey.MediaNext, false, false, false);

            if (periodicRedraw is not null && Environment.TickCount64 >= nextRedrawAt)
            {
                periodicRedraw();
                nextRedrawAt = Environment.TickCount64 + redrawIntervalMilliseconds;
            }

            Thread.Sleep(50);
        }
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
