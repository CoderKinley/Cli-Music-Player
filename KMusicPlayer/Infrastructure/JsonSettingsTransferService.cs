using System.Text.Json;
using KMusicPlayer.Application;

namespace KMusicPlayer.Infrastructure;

public sealed class JsonSettingsTransferService : ISettingsTransferService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> SupportedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "favorites.json",
        "history.json",
        "settings.json",
        "session.json",
        "lyrics.json",
        "local-library.json",
        "playlists.json"
    };
    private readonly string _dataDirectory;
    private readonly string _backupDirectory;

    public JsonSettingsTransferService(string dataDirectory, string backupDirectory)
    {
        _dataDirectory = dataDirectory;
        _backupDirectory = backupDirectory;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_backupDirectory);
        var files = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in SupportedFiles)
        {
            var path = Path.Combine(_dataDirectory, fileName);
            if (!File.Exists(path))
                continue;
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            files[fileName] = document.RootElement.Clone();
        }

        var backup = new SettingsBackup(1, DateTimeOffset.UtcNow, files);
        var filePath = Path.Combine(
            _backupDirectory,
            $"musik-settings-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await using var output = File.Create(filePath);
        await JsonSerializer.SerializeAsync(output, backup, JsonOptions, cancellationToken);
        return filePath;
    }

    public async Task ImportAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(backupPath.Trim().Trim('"'));
        await using var input = File.OpenRead(fullPath);
        var backup = await JsonSerializer.DeserializeAsync<SettingsBackup>(
            input, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The backup file is empty or invalid.");
        if (backup.Version != 1)
            throw new InvalidDataException($"Unsupported backup version: {backup.Version}.");
        if (backup.Files.Count == 0 || backup.Files.Keys.Any(name => !SupportedFiles.Contains(name)))
            throw new InvalidDataException("The file is not a valid Musik settings backup.");

        // Preserve the current state before replacing anything.
        await ExportAsync(cancellationToken);
        Directory.CreateDirectory(_dataDirectory);
        foreach (var fileName in SupportedFiles.Except(backup.Files.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var currentFile = Path.Combine(_dataDirectory, fileName);
            if (File.Exists(currentFile))
                File.Delete(currentFile);
        }
        foreach (var (fileName, contents) in backup.Files)
        {
            var destination = Path.Combine(_dataDirectory, fileName);
            var temporaryPath = destination + ".import";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(contents, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, destination, true);
        }
    }

    private sealed record SettingsBackup(
        int Version,
        DateTimeOffset ExportedAtUtc,
        Dictionary<string, JsonElement> Files);
}
