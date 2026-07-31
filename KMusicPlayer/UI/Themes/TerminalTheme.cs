namespace KMusicPlayer.UI.Themes;

public enum TerminalTheme
{
    Blue,
    Spotify,
    SoundCloud
}

public static class ThemeManager
{
    public const TerminalTheme Default = TerminalTheme.Blue;

    public static TerminalTheme Current { get; private set; } = Default;

    public static void Apply(TerminalTheme theme) => Current = theme;

    public static ConsoleColor Map(ConsoleColor color) =>
        Current switch
        {
            TerminalTheme.Blue => color,
            TerminalTheme.Spotify => color switch
            {
                ConsoleColor.Blue => ConsoleColor.DarkGreen,
                ConsoleColor.Cyan => ConsoleColor.Green,
                ConsoleColor.DarkCyan => ConsoleColor.DarkGreen,
                ConsoleColor.DarkBlue => ConsoleColor.DarkGreen,
                ConsoleColor.Yellow => ConsoleColor.Green,
                ConsoleColor.DarkYellow => ConsoleColor.DarkGreen,
                ConsoleColor.Gray => ConsoleColor.DarkGray,
                ConsoleColor.White => ConsoleColor.White,
                ConsoleColor.Black => ConsoleColor.Black,
                _ => color
            },
            TerminalTheme.SoundCloud => color switch
            {
                ConsoleColor.Blue => ConsoleColor.Red,
                ConsoleColor.Cyan => ConsoleColor.Red,
                ConsoleColor.DarkCyan => ConsoleColor.DarkRed,
                ConsoleColor.DarkBlue => ConsoleColor.DarkRed,
                ConsoleColor.Gray => ConsoleColor.DarkGray,
                _ => color
            },
            _ => color
        };
}
