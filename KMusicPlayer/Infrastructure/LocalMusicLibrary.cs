using System.Text.Json;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.Infrastructure;

public sealed class LocalMusicLibrary : ILocalMusicLibrary
{
    private static readonly HashSet<string> AudioExtensions = new(
        [".mp3", ".m4a", ".webm", ".flac", ".wav", ".ogg", ".opus", ".aac"],
        StringComparer.OrdinalIgnoreCase);
    private readonly string _settingsPath;

    public LocalMusicLibrary(string settingsPath, string defaultDirectory)
    {
        _settingsPath = settingsPath;
        DirectoryPath = LoadDirectory() ?? defaultDirectory;
    }

    public string DirectoryPath { get; private set; }

    public Task<IReadOnlyList<Track>> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(DirectoryPath))
            return Task.FromResult<IReadOnlyList<Track>>([]);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        var tracks = Directory.EnumerateFiles(DirectoryPath, "*", options)
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => new Track(
                LocalTrack.ToId(path),
                Path.GetFileNameWithoutExtension(path),
                Path.GetFileName(Path.GetDirectoryName(path)) ?? "Local Music",
                null))
            .ToList();
        return Task.FromResult<IReadOnlyList<Track>>(tracks);
    }

    public async Task SetDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(directoryPath.Trim().Trim('"'));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Folder not found: {fullPath}");

        DirectoryPath = fullPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporaryPath = _settingsPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(new LibrarySettings(fullPath), new JsonSerializerOptions
            {
                WriteIndented = true
            }),
            cancellationToken);
        File.Move(temporaryPath, _settingsPath, true);
    }

    private string? LoadDirectory()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;
            return JsonSerializer.Deserialize<LibrarySettings>(File.ReadAllText(_settingsPath))
                ?.DirectoryPath;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LibrarySettings(string DirectoryPath);
}

public static class LocalTrack
{
    private const string Prefix = "local:";

    public static string ToId(string filePath) => Prefix + Path.GetFullPath(filePath);

    public static bool TryGetPath(string id, out string filePath)
    {
        if (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            filePath = id[Prefix.Length..];
            return true;
        }

        filePath = string.Empty;
        return false;
    }
}
