using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.Infrastructure;

public sealed partial class LyricsService : ILyricsService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://lrclib.net/"),
        Timeout = TimeSpan.FromSeconds(8)
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LyricsService(string filePath)
    {
        _filePath = filePath;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Musik/1.1 (https://github.com/CoderKinley/Cli-Music-Player)");
    }

    public async Task<LyricsResult?> GetAsync(
        Track track,
        CancellationToken cancellationToken = default)
    {
        var saved = await LoadSavedAsync(track.Id, cancellationToken);
        if (saved is not null)
            return new LyricsResult(saved.Text, saved.IsManual);

        try
        {
            var query = new Dictionary<string, string>
            {
                ["track_name"] = track.Title,
                ["artist_name"] = track.Artist
            };
            if (track.Duration is { } duration)
                query["duration"] = Math.Round(duration.TotalSeconds).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

            var uri = "api/get?" + string.Join("&", query.Select(item =>
                $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<LrclibResponse>(
                cancellationToken: cancellationToken);
            var text = payload?.PlainLyrics;
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(payload?.SyncedLyrics))
                text = StripTimestamps().Replace(payload.SyncedLyrics, string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = Normalize(text);
            await SaveAsync(track, text, false, cancellationToken);
            return new LyricsResult(text, false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public Task SaveManualAsync(
        Track track,
        string lyrics,
        CancellationToken cancellationToken = default) =>
        SaveAsync(track, Normalize(lyrics), true, cancellationToken);

    private async Task<SavedLyrics?> LoadSavedAsync(
        string trackId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadUnsafeAsync(cancellationToken);
            return entries.GetValueOrDefault(trackId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(
        Track track,
        string text,
        bool isManual,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadUnsafeAsync(cancellationToken);
            entries[track.Id] = new SavedLyrics(track.Title, track.Artist, text, isManual);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporaryPath = _filePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, SavedLyrics>> LoadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, SavedLyrics>(StringComparer.Ordinal);
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, SavedLyrics>>(
            stream, JsonOptions, cancellationToken) ?? [];
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    public void Dispose()
    {
        _httpClient.Dispose();
        _gate.Dispose();
    }

    [GeneratedRegex(@"^\[(?:\d{1,2}:)?\d{1,2}:\d{2}(?:\.\d+)?\]\s*", RegexOptions.Multiline)]
    private static partial Regex StripTimestamps();

    private sealed record SavedLyrics(string Title, string Artist, string Text, bool IsManual);
    private sealed record LrclibResponse(string? PlainLyrics, string? SyncedLyrics);
}
