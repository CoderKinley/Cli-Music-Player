using System.Text;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;
using KMusicPlayer.Infrastructure;
using KMusicPlayer.UI;
using KMusicPlayer.UI.Screens;
using KMusicPlayer.UI.Themes;

Console.OutputEncoding = Encoding.UTF8;
if (args.Contains("--spectrum-preview"))
{
    var levels = new double[] { 0.8, 0.95, 0.6, 0.35, 0.7, 0.5, 0.4, 0.65, 0.3, 0.45, 0.2, 0.1 };
    var frames = Enum.GetValues<SpectrumStyle>().Where(style => style != SpectrumStyle.Off)
        .Select(style => new { Name = SpectrumPanel.Label(style), Rows = SpectrumPanel.Render(72, 18, style, levels) });
    Directory.CreateDirectory("artifacts");
    await File.WriteAllTextAsync("artifacts/spectrum-preview.json", System.Text.Json.JsonSerializer.Serialize(frames));
    Console.WriteLine("Spectrum preview frames saved in artifacts/spectrum-preview.json");
    return;
}
if (args.Contains("--width-probe"))
{
    foreach (var sample in new[] { "A", "ヨルシカ", "🎵", "👩‍🎤", "🇧🇹", "❤️" })
    {
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(sample);
        var cells = Console.CursorLeft;
        Console.WriteLine($" = {cells} host cells, {TerminalText.Width(sample)} measured cells");
    }
    return;
}
if (args.Contains("--input-probe"))
{
    Console.WriteLine($"INPUT RESULT: {SearchInput.Read()}");
    return;
}
if (args.Contains("--guide-probe"))
{
    GuideScreen.Show();
    Console.WriteLine("PASS: guide navigation and return.");
    return;
}

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition)
        throw new Exception(description);
    checks++;
}

foreach (var theme in new[] { TerminalTheme.Spotify, TerminalTheme.SoundCloud })
{
    ThemeManager.Apply(theme);
    Check(ThemeManager.Map(ConsoleColor.Gray) == ConsoleColor.White, "Theme dims primary text");
    Check(ThemeManager.Map(ConsoleColor.DarkGray) == ConsoleColor.Gray, "Theme dims shortcut instructions");
    Check(ThemeManager.Map(ConsoleColor.Black) == ConsoleColor.Black, "Theme changes the black background or selection text");
    Check(ThemeManager.Map(ConsoleColor.Cyan) != ConsoleColor.Black, "Theme hides selection highlights");
}
ThemeManager.Apply(ThemeManager.Default);

var panelWrites = new List<(int X, int Y, string Text, ConsoleColor Foreground, ConsoleColor Background)>();
var panel = new PlaybackPanel((x, y, text, foreground, background) => panelWrites.Add((x, y, text, foreground, background)));
var panelFrame = new PlaybackPanelFrame(10, 80, 1, TerminalTheme.Blue,
    " PLAYING | Vol 50% | Shuffle OFF ", "A long song title", "0:00 / 4:32", true, 0);
panel.Draw(panelFrame);
Check(panelWrites.Any(write => write.Y == 13), "First frame must draw the player border");
panelWrites.Clear();
for (var poll = 0; poll < 40; poll++)
    panel.Draw(panelFrame);
Check(panelWrites.Count == 0, "An unchanged player must not write to the terminal");
panelFrame = panelFrame with { Time = "0:01 / 4:32" };
panel.Draw(panelFrame);
Check(panelWrites.Count == 1 && panelWrites[0].Text == panelFrame.Time, "A timer tick repainted static player content");
panelWrites.Clear();
panelFrame = panelFrame with { Filled = 3 };
panel.Draw(panelFrame);
Check(panelWrites.Count == 1 && panelWrites[0].X == 2 && panelWrites[0].Text.Length == 3
    && panelWrites[0].Background == ConsoleColor.Blue, "Progress must paint only newly filled cells");
panelWrites.Clear();
panelFrame = panelFrame with { Filled = 1 };
panel.Draw(panelFrame);
Check(panelWrites.Count == 1 && panelWrites[0].X == 3 && panelWrites[0].Text == "░░"
    && panelWrites[0].Background == ConsoleColor.Black, "Backward seeking must restore only emptied cells");
panelWrites.Clear();
panelFrame = panelFrame with { CanvasVersion = 2 };
panel.Draw(panelFrame);
Check(panelWrites.Any(write => write.Y == 13), "Returning from a cleared screen must restore the player");
panelWrites.Clear();
panelFrame = panelFrame with { Theme = TerminalTheme.Spotify };
panel.Draw(panelFrame);
Check(panelWrites.Any(write => write.Y == 13), "A theme change must repaint the player");

// Compare the resulting cells with a fresh paint to catch stale title, timer, and progress remnants.
var panelCells = new Dictionary<(int X, int Y), (char Text, ConsoleColor Foreground, ConsoleColor Background)>();
var incrementalPanel = new PlaybackPanel((x, y, text, foreground, background) =>
{
    for (var index = 0; index < text.Length; index++)
        panelCells[(x + index, y)] = (text[index], foreground, background);
});
foreach (var frame in new[]
{
    panelFrame,
    panelFrame with { Title = new string('a', 200), Time = "59:59 / 1:00:01", Filled = 75 },
    panelFrame with { Title = "[PAUSED] Short", Heading = " PAUSED | Vol 100% | Shuffle ON ", Time = "1:00:00 / 1:00:01", Filled = 76 },
    panelFrame with { Title = "Ready", Heading = " READY ", Time = "0:00 / --:--", IsPlaying = false, Filled = 0 },
    panelFrame with { Width = 60, Top = 11 },
    panelFrame with { Width = 100, Top = 12, Theme = TerminalTheme.SoundCloud }
})
{
    incrementalPanel.Draw(frame);
    var freshPanel = new PlaybackPanel((x, y, text, foreground, background) =>
    {
        Check(x >= 0 && x + text.Length <= frame.Width, "Player write exceeds its panel");
        for (var index = 0; index < text.Length; index++)
            Check(panelCells.TryGetValue((x + index, y), out var cell) && cell == (text[index], foreground, background),
                "Incremental player differs from a fresh frame");
    });
    freshPanel.Draw(frame);
}

foreach (var (text, expected) in new (string, int)[]
{
    ("Coldplay", 8), ("ヨルシカ", 8), ("中文", 4), ("e\u0301", OperatingSystem.IsWindows() ? 2 : 1),
    ("🎵", 2), ("👩‍🎤", OperatingSystem.IsWindows() ? 5 : 2),
    ("🇧🇹", OperatingSystem.IsWindows() ? 4 : 2), ("❤️", 2), ("♥", 1), ("⚡", 2)
})
    Check(TerminalText.Width(text) == expected, $"Wrong terminal width for {text}");

var samples = new[]
{
    "Short song", new string('x', 500), "ヨルシカ - 春泥棒 (OFFICIAL VIDEO)",
    "Lofi 🎵 👩‍🎤 🇧🇹 ❤️ 中中文 - a very long artist or author name",
    string.Concat(Enumerable.Repeat("e\u0301", 50)), "a\r\nb\tc\u001b[2J"
};
foreach (var sample in samples)
foreach (var width in Enumerable.Range(0, 161))
{
    var fitted = TerminalText.Fit(sample, width);
    Check(TerminalText.Width(fitted) <= width, "Fitted text overflows its cells");
    Check(TerminalText.Width(TerminalText.PadRight(sample, width)) == width, "Padding overflows or underfills");
    Check(TerminalText.Width(TerminalText.Tail(sample, width)) <= width, "Input tail overflows");
    Check(!fitted.Any(char.IsControl), "Metadata contains terminal controls");
    Check(!fitted.Contains('\uFFFD'), "Fitting damaged a Unicode grapheme");
}
Check(TerminalText.Fit("ヨルシカ", 6) == "ヨ...", "Wide truncation must leave room for ellipsis");
Check(TerminalText.Fit("👩‍🎤 music", TerminalText.Width("👩‍🎤") + 3).StartsWith("👩‍🎤"), "A joined emoji was split");
Check(TerminalText.Fit("e\u0301x", TerminalText.Width("e\u0301")) == "e\u0301", "A combining sequence was split");
Check(TerminalText.Fit("text", -1) == "", "Negative budgets must be empty");
foreach (var title in samples)
foreach (var artist in samples)
foreach (var width in new[] { 0, 4, 12, 20, 40, 80, 120 })
    Check(TerminalText.Width(TerminalText.TrackLabel(title, artist, width)) <= width,
        "The combined title and artist overflow the row");
var label = TerminalText.TrackLabel(new string('x', 200), "Coldplay", 40);
Check(label.EndsWith(" - Coldplay"), "Long titles should preserve a short artist");
Check(TerminalText.TrackLabel("Yellow", new string('a', 300), 40).StartsWith("Yellow - "),
    "A long artist should preserve a short title");

foreach (var sample in samples.Take(5))
foreach (var width in Enumerable.Range(5, 76))
{
    var wrapped = TerminalText.Wrap(sample, width);
    Check(wrapped.All(line => TerminalText.Width(line) <= width), "Wrapped text overflows its row");
    Check(string.Concat(wrapped) == sample, "Wrapping lost song or artist text");
}
Check(TerminalText.Wrap("first\n\nlast", 20).SequenceEqual(new[] { "first", "", "last" }), "Wrapping lost empty paragraphs");
Check(GuideCatalog.Find("shuffle").Any(entry => entry.Keys == "R"), "Guide search cannot find shuffle");
Check(GuideCatalog.Find("import", "Library").Any(entry => entry.Keys.Contains("URL")), "Guide cannot find playlist import instructions");
Check(GuideCatalog.Find("SHIFT+TAB").Any(), "Guide combinations must be searchable without case sensitivity");
Check(!GuideCatalog.Find("no-such-shortcut-1234").Any(), "Unknown guide terms should not return unrelated entries");
Check(GuideCatalog.Find("playlist remove").Any(), "Guide search must combine keyword terms");
foreach (var height in Enumerable.Range(20, 81))
{
    Check(TerminalLayout.PlayerTop(height) + 9 == height, "Footer overlaps the player or terminal bottom");
    Check(4 + TerminalLayout.SongRows(height) == TerminalLayout.PlayerTop(height) - 1, "Song rows overlap the panel border");
}
Check(!TerminalLayout.Fits(59, 20) && !TerminalLayout.Fits(80, 19) && TerminalLayout.Fits(60, 20), "Small-window layout boundary is incorrect");
Check(ListNavigation.Page(0, 0, 10, 1) == 0, "Paging an empty list must be safe");
Check(ListNavigation.Page(15, 20, 10, 1) == 19, "Page down did not clamp to the final song");
Check(ListNavigation.Page(5, 20, 10, -1) == 0, "Page up did not clamp to the first song");
Check(ListNavigation.FirstVisible(19, 20, 5) == 15, "Last-page scrolling must keep the selection visible");

var testDirectory = Path.Combine(Path.GetTempPath(), "Musik-Regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);
try
{
    var a = new Track("a", "First", "Artist", null);
    var b = new Track("b", "Second", "Artist", TimeSpan.FromMinutes(3));
    var c = new Track("c", "Third", "Artist", TimeSpan.FromMinutes(4));
    var path = Path.Combine(testDirectory, "playlists.json");
    var repository = new JsonPlaylistRepository(path);
    await repository.SaveAsync(new SavedPlaylist("one", "Playlist", "source-url", [a, b, a, c]));
    await repository.SaveAsync(new SavedPlaylist("two", "Other playlist", null, [b]));
    Check(await repository.RemoveTrackAsync("one", 2) == a, "Wrong duplicate occurrence removed");
    var saved = await new JsonPlaylistRepository(path).GetAllAsync();
    Check(saved[0].Tracks.SequenceEqual(new[] { a, b, c }), "Removal was not persisted in order");
    Check(saved[0].Name == "Playlist" && saved[0].SourceUrl == "source-url", "Removal changed playlist metadata");
    Check(saved[1].Tracks.SequenceEqual(new[] { b }), "Removal changed another playlist");
    var beforeInvalid = await File.ReadAllTextAsync(path);
    foreach (var index in new[] { -1, 10 })
    {
        var rejected = false;
        try { await repository.RemoveTrackAsync("one", index); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "An invalid track index should fail");
    }
    Check(await File.ReadAllTextAsync(path) == beforeInvalid, "Invalid removal changed the saved file");
    await repository.RemoveTrackAsync("one", 1);
    await repository.RemoveTrackAsync("one", 1);
    await repository.RemoveTrackAsync("one", 0);
    saved = await new JsonPlaylistRepository(path).GetAllAsync();
    Check(saved[0].Tracks.Count == 0 && saved.Count == 2, "Removing the final song should preserve an empty playlist");

    var player = new FakePlayer();
    var music = new MusicApplication(new FakeSource(),
        new JsonFavoriteRepository(Path.Combine(testDirectory, "favorites.json")), player,
        new JsonHistoryRepository(Path.Combine(testDirectory, "history.json")),
        new JsonSettingsRepository(Path.Combine(testDirectory, "settings.json")),
        new JsonPlaybackSessionRepository(Path.Combine(testDirectory, "session.json")));
    if (args.Contains("--playlist-probe") || args.Contains("--ux-probe"))
    {
        var longTrack = new Track("long", string.Concat(Enumerable.Repeat("ヨルシカ 🎵 春泥棒 ", 15)),
            string.Concat(Enumerable.Repeat("Very long artist ヨルシカ 👩‍🎤 ", 15)), TimeSpan.FromMinutes(4));
        var uxProbe = args.Contains("--ux-probe");
        IReadOnlyList<Track> probeTracks = uxProbe
            ? new[] { longTrack }.Concat(Enumerable.Range(1, 30).Select(index => new Track($"probe-{index}", $"Song {index}", "Test artist", null))).ToList()
            : [longTrack, b];
        await repository.SaveAsync(new SavedPlaylist("one", "UI regression playlist", "source-url", probeTracks));
        var favorites = new JsonFavoriteRepository(Path.Combine(testDirectory, "favorites.json"));
        await favorites.AddAsync(longTrack);
        await music.InitializeAsync();
        using var lyrics = new LyricsService(Path.Combine(testDirectory, "lyrics.json"));
        var terminal = new TerminalApplication(music, lyrics,
            new YouTubeTrackDownloadService(testDirectory),
            new LocalMusicLibrary(Path.Combine(testDirectory, "local-library.json"), testDirectory),
            new JsonSettingsTransferService(testDirectory, Path.Combine(testDirectory, "backups")), repository);
        await terminal.RunAsync();
        saved = await new JsonPlaylistRepository(path).GetAllAsync();
        if (uxProbe)
        {
            Check(saved[0].Tracks.Count == 31, "Browsing help or details changed the playlist");
            Check(player.CurrentTrack?.Id == "probe-30", "End navigation or returning from details changed the selected song");
            Check((await favorites.GetAllAsync()).Any(track => track.Id == "probe-30"), "Favorite action did not apply to the selected song");
            Console.WriteLine("PASS: interactive guide, details, end/page navigation, playback selection, and favorites.");
        }
        else
        {
            Check(saved[0].Tracks.Count == 0 && saved[1].Tracks.Count == 1, "UI deletion did not persist correctly");
            Check((await favorites.GetAllAsync()).Single() == longTrack, "Playlist removal changed favorites");
            Console.WriteLine("PASS: interactive playlist removal, empty-list navigation, saved counts, and favorites isolation.");
        }
        return;
    }
    var queue = new PlaybackQueueController(music);
    await queue.StartAsync([a, b, c], 0, true, "one");
    queue.RemoveTrack("two", 1);
    Check(await queue.NextAsync() == b, "Editing another playlist changed the active queue");
    await queue.StartAsync([a, b, c], 0, true, "one");
    queue.RemoveTrack("one", 1);
    Check(await queue.NextAsync() == c, "The queue still plays a removed song");
    Check(await queue.NextAsync() == a, "Looping after removal failed");
    await queue.StartAsync([a, b, c], 1, true, "one");
    queue.RemoveTrack("one", 1);
    Check(player.CurrentTrack == b && player.IsPlaying, "Removal interrupted the current song");
    Check(await queue.NextAsync() == c, "Next skipped the song after a removed current track");
    await queue.StartAsync([a, b, c], 1, true, "one");
    queue.RemoveTrack("one", 1);
    Check(await queue.PreviousAsync() == a, "Previous skipped the song before a removed current track");
    await queue.StartAsync([a, b, c], 1, true, "one");
    queue.RemoveTrack("one", 0);
    Check(await queue.NextAsync() == c, "Removing an earlier song moved the queue index incorrectly");
    queue.SetShuffle(true);
    for (var attempt = 0; attempt < 30; attempt++)
    {
        await queue.StartAsync([a, b, c], 0, true, "one");
        queue.RemoveTrack("one", 0);
        Check(await queue.NextAsync() is { Id: "b" or "c" }, "Shuffle failed after deleting the first current track");
        await queue.StartAsync([a, b, c], 0, true, "one");
        queue.RemoveTrack("one", 0);
        Check(await queue.PreviousAsync() is { Id: "b" or "c" }, "Shuffle previous failed after deleting the current track");
    }
    await queue.StartAsync([a], 0, true, "one");
    queue.RemoveTrack("one", 0);
    Check(!queue.HasTracks && await queue.NextAsync() is null && await queue.PreviousAsync() is null,
        "An emptied queue must be safe to navigate");

    await using var automatic = new PlaybackQueueController(music);
    await automatic.StartAsync([a, b, c], 0, false);
    automatic.StartAutomaticPlayback();
    async Task WaitForAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Check(condition(), description);
    }
    await WaitForAsync(() => player.PreparedTrack == b, "Background queue did not preload the next track");
    player.ShouldTransition = true;
    await WaitForAsync(() => player.CurrentTrack == b, "Queue did not transition while the UI was idle");
    await WaitForAsync(() => player.PreparedTrack == c, "Queue did not preload after advancing");
    player.ShouldTransition = true;
    await WaitForAsync(() => player.CurrentTrack == c, "Queue skipped or repeated a track during automatic transitions");
    player.ShouldTransition = true;
    await WaitForAsync(() => !automatic.HasTracks && !player.IsPlaying, "Final track did not finish its queue");
    await automatic.StartAsync([a, b], 0, true);
    player.ShouldTransition = true;
    await WaitForAsync(() => player.CurrentTrack == b, "Loop queue did not advance");
    player.ShouldTransition = true;
    await WaitForAsync(() => player.CurrentTrack == a, "Loop queue did not return to its first track");
    automatic.Stop();
    await Task.Delay(200);
    Check(!player.IsPlaying && !automatic.HasTracks, "Stopped background queue restarted playback");
}
finally
{
    Directory.Delete(testDirectory, recursive: true);
}
try { await AudioRegression.RunAsync(Check, args.Contains("--audio-probe")); }
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
    return;
}
Console.WriteLine($"PASS: {checks} regression checks (terminal text, playback rendering, themes, playlists, queues, spectrum, audio settings, fades).");

sealed class FakeSource : IMusicSource
{
    public Task<IReadOnlyList<Track>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Track>>([]);
    public Task<string> GetPlayableSourceAsync(Track track, CancellationToken cancellationToken = default) =>
        Task.FromResult(track.Id);
    public Task<PlaylistResult> GetPlaylistAsync(string playlistUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PlaylistResult("Playlist", []));
}

sealed class FakePlayer : IAudioPlayer
{
    public Track? CurrentTrack { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public int Volume { get; private set; } = 50;
    public TimeSpan Elapsed => TimeSpan.Zero;
    public bool ShouldTransition { get; set; }
    public Track? PreparedTrack { get; private set; }
    public Task PrepareAsync(Track track, CancellationToken cancellationToken = default)
    {
        PreparedTrack = track;
        return Task.CompletedTask;
    }
    public Task PlayAsync(Track track, TimeSpan? startPosition = null, CancellationToken cancellationToken = default)
    {
        CurrentTrack = track;
        IsPlaying = true;
        ShouldTransition = false;
        return Task.CompletedTask;
    }
    public void ChangeVolume(int delta) => Volume += delta;
    public void SetVolume(int volume) => Volume = volume;
    public void TogglePause() => IsPaused = !IsPaused;
    public void Seek(TimeSpan offset) { }
    public void Stop() => IsPlaying = false;
}
