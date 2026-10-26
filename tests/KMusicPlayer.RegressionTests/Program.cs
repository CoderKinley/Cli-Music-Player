using System.Text;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;
using KMusicPlayer.Infrastructure;
using KMusicPlayer.UI;
using KMusicPlayer.UI.Screens;

Console.OutputEncoding = Encoding.UTF8;
if (args.Contains("--input-probe"))
{
    Console.WriteLine($"INPUT RESULT: {SearchInput.Read()}");
    return;
}

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition)
        throw new Exception(description);
    checks++;
}

foreach (var (text, expected) in new (string, int)[]
{
    ("Coldplay", 8), ("ヨルシカ", 8), ("中文", 4), ("e\u0301", 1),
    ("🎵", 2), ("👩‍🎤", 2), ("🇧🇹", 2), ("❤️", 2), ("♥", 1), ("⚡", 2)
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
Check(TerminalText.Fit("👩‍🎤 music", 2) == "👩‍🎤", "A joined emoji was split");
Check(TerminalText.Fit("e\u0301x", 1) == "e\u0301", "A combining sequence was split");
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
    if (args.Contains("--playlist-probe"))
    {
        var longTrack = new Track("long", string.Concat(Enumerable.Repeat("ヨルシカ 🎵 春泥棒 ", 15)),
            string.Concat(Enumerable.Repeat("Very long artist ヨルシカ 👩‍🎤 ", 15)), TimeSpan.FromMinutes(4));
        await repository.SaveAsync(new SavedPlaylist("one", "UI regression playlist", "source-url", [longTrack, b]));
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
        Check(saved[0].Tracks.Count == 0 && saved[1].Tracks.Count == 1, "UI deletion did not persist correctly");
        Check((await favorites.GetAllAsync()).Single() == longTrack, "Playlist removal changed favorites");
        Console.WriteLine("PASS: interactive playlist removal, empty-list navigation, saved counts, and favorites isolation.");
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
}
finally
{
    Directory.Delete(testDirectory, recursive: true);
}
Console.WriteLine($"PASS: {checks} regression checks (terminal text, playlist persistence, playback queues).");

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
    public Task PlayAsync(Track track, TimeSpan? startPosition = null, CancellationToken cancellationToken = default)
    {
        CurrentTrack = track;
        IsPlaying = true;
        return Task.CompletedTask;
    }
    public void ChangeVolume(int delta) => Volume += delta;
    public void SetVolume(int volume) => Volume = volume;
    public void TogglePause() => IsPaused = !IsPaused;
    public void Seek(TimeSpan offset) { }
    public void Stop() => IsPlaying = false;
}
