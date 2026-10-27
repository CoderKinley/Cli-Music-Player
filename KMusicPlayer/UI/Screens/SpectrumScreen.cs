using KMusicPlayer.Application;

namespace KMusicPlayer.UI.Screens;

public sealed class SpectrumScreen(MusicApplication music)
{
    private readonly SpectrumPanel _preview = new();

    public async Task ShowAsync()
    {
        var styles = Enum.GetValues<SpectrumStyle>();
        var selected = Enum.TryParse<SpectrumStyle>(music.AudioPreferences.Spectrum, out var saved) ? (int)saved : 0;
        var width = -1;
        var height = -1;
        while (true)
        {
            var key = TerminalInput.ReadResponsive(() => Draw(selected), ref width, ref height,
                periodicRedraw: () => DrawPreview(selected), redrawIntervalMilliseconds: 16);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selected = (selected + styles.Length + 1) % (styles.Length + 2);
                    DrawOptions(selected);
                    break;
                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % (styles.Length + 2);
                    DrawOptions(selected);
                    break;
                case ConsoleKey.Enter:
                    var preferences = music.AudioPreferences;
                    if (selected < styles.Length)
                        preferences = preferences with { Spectrum = styles[selected].ToString() };
                    else if (selected == styles.Length)
                    {
                        double[] durations = [0, 1.5, 3, 5];
                        var index = Array.FindIndex(durations, value => value == preferences.CrossfadeSeconds);
                        preferences = preferences with { CrossfadeSeconds = durations[(index + 1) % durations.Length] };
                    }
                    else preferences = preferences with { TrimSilence = !preferences.TrimSilence };
                    await music.SetAudioPreferencesAsync(preferences);
                    DrawOptions(selected);
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return;
                case ConsoleKey.Spacebar:
                    music.TogglePause();
                    await music.SavePlaybackSessionAsync();
                    break;
                case ConsoleKey.OemPlus:
                case ConsoleKey.Add:
                    await music.ChangeVolumeAsync(5);
                    break;
                case ConsoleKey.OemMinus:
                case ConsoleKey.Subtract:
                    await music.ChangeVolumeAsync(-5);
                    break;
            }
        }
    }

    private void Draw(int selected)
    {
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, TerminalCanvas.Height - 1, " SPECTRUM & TRANSITIONS ");
        TerminalCanvas.WriteAt(2, 2, "Up/Down: Select  Enter: Apply/change  Q: Back", ConsoleColor.DarkGray);
        var previewX = Math.Min(31, TerminalCanvas.Width / 2);
        TerminalCanvas.DrawBox(previewX, 4, TerminalCanvas.Width - previewX - 2, TerminalCanvas.Height - 9, "");
        DrawOptions(selected);
        DrawPreview(selected);
        TerminalCanvas.WriteAt(2, TerminalCanvas.Height - 4, "Live preview. Space: Pause  +/-: Volume", ConsoleColor.DarkGray);
        TerminalCanvas.WriteAt(2, TerminalCanvas.Height - 3, "Silence: edges only. Crossfade: overlapping track audio.", ConsoleColor.DarkGray);
    }

    private void DrawOptions(int selected)
    {
        var styles = Enum.GetValues<SpectrumStyle>();
        var leftWidth = Math.Min(31, TerminalCanvas.Width / 2) - 3;
        for (var index = 0; index < styles.Length + 2; index++)
        {
            var text = index < styles.Length
                ? SpectrumPanel.Label(styles[index]) + (music.AudioPreferences.Spectrum == styles[index].ToString() ? " *" : "")
                : index == styles.Length
                    ? $"Crossfade: {(music.AudioPreferences.CrossfadeSeconds == 0 ? "Off" : $"{music.AudioPreferences.CrossfadeSeconds:0.#}s")}"
                    : $"Trim silence: {(music.AudioPreferences.TrimSilence ? "On" : "Off")}";
            var y = index < styles.Length ? 5 + index : 7 + index;
            TerminalCanvas.WriteAt(2, y, TerminalText.PadRight($"{(index == selected ? ">" : " ")} {text}", leftWidth),
                index == selected ? ConsoleColor.Black : ConsoleColor.Gray,
                index == selected ? ConsoleColor.Cyan : ConsoleColor.Black);
        }
    }

    private void DrawPreview(int selected)
    {
        var previewX = Math.Min(31, TerminalCanvas.Width / 2);
        var styles = Enum.GetValues<SpectrumStyle>();
        var style = selected < styles.Length ? styles[selected]
            : Enum.TryParse<SpectrumStyle>(music.AudioPreferences.Spectrum, out var saved) ? saved : SpectrumStyle.CatOutline;
        _preview.Draw(previewX + 1, 5, TerminalCanvas.Width - previewX - 4, TerminalCanvas.Height - 11, style, music.Spectrum);
    }
}
