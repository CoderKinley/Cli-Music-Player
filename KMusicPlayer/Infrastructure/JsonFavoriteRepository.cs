using System.Text.Json;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.Infrastructure;

public sealed class JsonFavoriteRepository : IFavoriteRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFavoriteRepository(string filePath) => _filePath = filePath;

    public async Task<IReadOnlyList<Track>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ContainsAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        var tracks = await GetAllAsync(cancellationToken);
        return tracks.Any(track => track.Id == trackId);
    }

    public async Task AddAsync(Track track, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tracks = await LoadUnsafeAsync(cancellationToken);
            if (tracks.All(item => item.Id != track.Id))
            {
                tracks.Add(track);
                await SaveUnsafeAsync(tracks, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string trackId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tracks = await LoadUnsafeAsync(cancellationToken);
            if (tracks.RemoveAll(track => track.Id == trackId) > 0)
                await SaveUnsafeAsync(tracks, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Track>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Track>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];
    }

    private async Task SaveUnsafeAsync(
        List<Track> tracks,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                tracks,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, _filePath, true);
    }
}
