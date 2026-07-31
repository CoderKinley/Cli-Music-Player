using System.Text.Json;
using KMusicPlayer.Application;

namespace KMusicPlayer.Infrastructure;

public sealed class JsonSettingsRepository : ISettingsRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;

    public JsonSettingsRepository(string filePath) => _filePath = filePath;

    public async Task<int> GetVolumeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        return Math.Clamp(settings?.Volume ?? 70, 0, 100);
    }

    public async Task SaveVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        await SaveAsync(
            new PlayerSettings(
                Math.Clamp(volume, 0, 100),
                current?.Shuffle ?? false,
                current?.Theme ?? "Blue"),
            cancellationToken);
    }

    public async Task<bool> GetShuffleAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken))?.Shuffle ?? false;

    public async Task SaveShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        await SaveAsync(
            new PlayerSettings(current?.Volume ?? 70, enabled, current?.Theme ?? "Blue"),
            cancellationToken);
    }

    public async Task<string> GetThemeAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken))?.Theme ?? "Blue";

    public async Task SaveThemeAsync(string theme, CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        await SaveAsync(
            new PlayerSettings(current?.Volume ?? 70, current?.Shuffle ?? false, theme),
            cancellationToken);
    }

    private async Task<PlayerSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return null;

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<PlayerSettings>(
            stream,
            Options,
            cancellationToken);
    }

    private async Task SaveAsync(
        PlayerSettings settings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }

    private sealed record PlayerSettings(
        int Volume,
        bool Shuffle = false,
        string Theme = "Blue");
}
