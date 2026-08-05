using KMusicPlayer.Application;
using KMusicPlayer.Domain;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace KMusicPlayer.Infrastructure;

public sealed class YouTubeTrackDownloadService : ITrackDownloadService
{
    private readonly YoutubeClient _youtube = new();
    private readonly string _downloadDirectory;

    public YouTubeTrackDownloadService(string downloadDirectory) =>
        _downloadDirectory = downloadDirectory;

    public async Task<TrackDownloadResult> DownloadAsync(
        Track track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = await _youtube.Videos.Streams.GetManifestAsync(track.Id, cancellationToken);
        var stream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate()
            ?? throw new InvalidOperationException("No downloadable audio stream was found.");

        Directory.CreateDirectory(_downloadDirectory);
        var baseName = SanitizeFileName($"{track.Artist} - {track.Title}");
        var filePath = Path.Combine(_downloadDirectory, $"{baseName}.{stream.Container.Name}");
        if (File.Exists(filePath))
            return new TrackDownloadResult(filePath, true);

        var temporaryPath = filePath + ".part";
        try
        {
            await _youtube.Videos.Streams.DownloadAsync(
                stream, temporaryPath, progress, cancellationToken);
            File.Move(temporaryPath, filePath);
            return new TrackDownloadResult(filePath, false);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "music" : sanitized;
    }
}
