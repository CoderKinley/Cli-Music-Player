using System.Text.Json;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.Infrastructure;

public sealed class JsonHistoryRepository : IHistoryRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;

    public JsonHistoryRepository(string filePath) => _filePath = filePath;

    public async Task<IReadOnlyList<Track>> GetRecentAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Track>>(
            stream,
            Options,
            cancellationToken) ?? [];
    }

    public async Task SaveRecentAsync(
        IReadOnlyList<Track> tracks,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, tracks, Options, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }
}
