using System.Text.Json;
using KMusicPlayer.Application;

namespace KMusicPlayer.Infrastructure;

public sealed class JsonPlaybackSessionRepository : IPlaybackSessionRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;

    public JsonPlaybackSessionRepository(string filePath) => _filePath = filePath;

    public async Task<PlaybackSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return null;

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<PlaybackSession>(
            stream,
            Options,
            cancellationToken);
    }

    public async Task SaveAsync(
        PlaybackSession session,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, session, Options, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }
}
