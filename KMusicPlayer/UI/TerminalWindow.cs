namespace KMusicPlayer.UI;

public static class TerminalWindow
{
    public static void Configure(int width, int height)
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
            return;

        try
        {
            var targetWidth = Math.Max(1, Math.Min(width, Console.LargestWindowWidth));
            var targetHeight = Math.Max(1, Math.Min(height, Console.LargestWindowHeight));

            // The buffer must be large enough before the window can be enlarged.
            Console.SetBufferSize(
                Math.Max(Console.BufferWidth, targetWidth),
                Math.Max(Console.BufferHeight, targetHeight));
            Console.SetWindowSize(targetWidth, targetHeight);

            // Keep the buffer aligned with the viewport so the TUI does not scroll.
            Console.SetBufferSize(targetWidth, targetHeight);
        }
        catch (Exception exception) when (
            exception is IOException or
            PlatformNotSupportedException or
            ArgumentOutOfRangeException or
            System.Security.SecurityException)
        {
            // Some terminal hosts control their own dimensions. The responsive
            // renderer will use whatever size that host provides.
        }
    }
}
