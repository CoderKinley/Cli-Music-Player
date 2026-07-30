namespace KMusicPlayer.UI.Themes;

public enum TerminalTheme
{
    Spotify,
    SoundCloud
}

public static class ThemeManager
{
    public static TerminalTheme Current { get; private set; } = TerminalTheme.Spotify;

    public static void Apply(TerminalTheme theme) => Current = theme;

    public static ConsoleColor Map(ConsoleColor color) =>
        Current switch
        {
            TerminalTheme.Spotify => color switch
            {
                ConsoleColor.Blue => ConsoleColor.DarkGreen,
                ConsoleColor.Cyan => ConsoleColor.Green,
                ConsoleColor.DarkCyan => ConsoleColor.DarkGreen,
                _ => color
            },
            TerminalTheme.SoundCloud => color switch
            {
                ConsoleColor.Blue => ConsoleColor.DarkYellow,
                ConsoleColor.Cyan => ConsoleColor.Yellow,
                ConsoleColor.DarkCyan => ConsoleColor.DarkYellow,
                _ => color
            },
            _ => color
        };
}
