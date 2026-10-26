namespace KMusicPlayer.UI;

public static class TerminalLayout
{
    public const int MinimumWidth = 60;
    public const int MinimumHeight = 20;
    public static bool Fits(int width, int height) => width >= MinimumWidth && height >= MinimumHeight;
    public static int PlayerTop(int height) => Math.Max(1, height - 9);
    public static int SongRows(int height) => Math.Max(1, PlayerTop(height) - 5);
}
