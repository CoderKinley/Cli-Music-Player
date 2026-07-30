using KMusicPlayer.Application;
using KMusicPlayer.UI.Themes;

namespace KMusicPlayer.UI.Screens;

public sealed class ThemesScreen
{
    private readonly MusicApplication _music;

    public ThemesScreen(MusicApplication music) => _music = music;

    public async Task ShowAsync()
    {
        var themes = Enum.GetValues<TerminalTheme>();
        var selected = Array.IndexOf(themes, ThemeManager.Current);
        Draw(themes, selected);

        while (true)
        {
            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.UpArrow:
                    selected = selected == 0 ? themes.Length - 1 : selected - 1;
                    DrawOptions(themes, selected);
                    break;
                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % themes.Length;
                    DrawOptions(themes, selected);
                    break;
                case ConsoleKey.Enter:
                    ThemeManager.Apply(themes[selected]);
                    await _music.SetThemeAsync(themes[selected].ToString());
                    TerminalCanvas.Clear();
                    Draw(themes, selected);
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    private static void Draw(IReadOnlyList<TerminalTheme> themes, int selected)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 9, " THEMES ");
        TerminalCanvas.WriteAt(
            2, 2,
            "Up/Down: Select    Enter: Apply    Q: Home",
            ConsoleColor.DarkGray);
        DrawOptions(themes, selected);
    }

    private static void DrawOptions(IReadOnlyList<TerminalTheme> themes, int selected)
    {
        for (var index = 0; index < themes.Count; index++)
        {
            var active = index == selected;
            var applied = themes[index] == ThemeManager.Current ? " [SELECTED]" : "";
            TerminalCanvas.WriteAt(
                2,
                4 + index,
                $"{(active ? ">" : " ")} {themes[index]}{applied}".PadRight(32),
                active ? ConsoleColor.Black : ConsoleColor.Gray,
                active ? ConsoleColor.Cyan : ConsoleColor.Black);
        }
    }
}
