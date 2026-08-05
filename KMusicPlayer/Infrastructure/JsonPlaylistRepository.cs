using System.Text.Json;
using KMusicPlayer.Application;

namespace KMusicPlayer.Infrastructure;

public sealed class JsonPlaylistRepository : IPlaylistRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPlaylistRepository(string filePath) => _filePath = filePath;

    public async Task<IReadOnlyList<SavedPlaylist>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(
        SavedPlaylist playlist,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var playlists = await LoadUnsafeAsync(cancellationToken);
            var index = playlists.FindIndex(item => item.Id == playlist.Id);
            if (index >= 0)
                playlists[index] = playlist;
            else
                playlists.Add(playlist);
            await SaveUnsafeAsync(playlists, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var playlists = await LoadUnsafeAsync(cancellationToken);
            if (playlists.RemoveAll(item => item.Id == playlistId) > 0)
                await SaveUnsafeAsync(playlists, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<SavedPlaylist>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return [];
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<SavedPlaylist>>(
            stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task SaveUnsafeAsync(
        List<SavedPlaylist> playlists,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, playlists, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }
}
