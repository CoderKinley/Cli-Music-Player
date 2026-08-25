using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace KMusicPlayer.Infrastructure;

public sealed class YouTubeTrackDownloadService : ITrackDownloadService
{
    private static readonly Regex ProgressRegex = new(@"\[download\]\s+([0-9\.]+)%", RegexOptions.Compiled);
    private readonly YoutubeClient _youtube = new();
    private readonly string _downloadDirectory;

    public YouTubeTrackDownloadService(string downloadDirectory) =>
        _downloadDirectory = downloadDirectory;

    public async Task<TrackDownloadResult> DownloadAsync(
        Track track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_downloadDirectory);
        var baseName = SanitizeFileName($"{track.Artist} - {track.Title}");

        // Check if an existing downloaded file already exists for this track
        var existingFile = FindExistingTrackFile(baseName);
        if (existingFile != null)
            return new TrackDownloadResult(existingFile, true);

        // Try downloading with yt-dlp first (most reliable with modern YouTube bot protections)
        var ytdlPath = await ToolResolver.EnsureYtDlpAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(ytdlPath))
        {
            try
            {
                var downloadedFile = await DownloadWithYtDlpAsync(
                    ytdlPath,
                    track,
                    baseName,
                    progress,
                    cancellationToken);

                if (File.Exists(downloadedFile))
                    return new TrackDownloadResult(downloadedFile, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // If yt-dlp encounters an error, attempt fallback to YoutubeExplode
            }
        }

        // Fallback to YoutubeExplode
        return await DownloadWithYoutubeExplodeAsync(track, baseName, progress, cancellationToken);
    }

    private async Task<string> DownloadWithYtDlpAsync(
        string ytdlPath,
        Track track,
        string baseName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var outputTemplate = Path.Combine(_downloadDirectory, $"{baseName}.%(ext)s");
        var videoUrl = $"https://www.youtube.com/watch?v={track.Id}";

        var startInfo = new ProcessStartInfo
        {
            FileName = ytdlPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("ba/b");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--progress");
        if (ToolResolver.HasNodeJs())
        {
            startInfo.ArgumentList.Add("--js-runtimes");
            startInfo.ArgumentList.Add("node");
        }
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputTemplate);
        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add("after_move:filepath");
        startInfo.ArgumentList.Add(videoUrl);

        using var process = new Process { StartInfo = startInfo };
        string? resolvedFilePath = null;

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            var match = ProgressRegex.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                progress?.Report(percent / 100.0);
            }
            else
            {
                var candidate = e.Data.Trim();
                if (File.Exists(candidate))
                {
                    resolvedFilePath = candidate;
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"yt-dlp download failed with exit code {process.ExitCode}: {stderr}");
        }

        if (!string.IsNullOrWhiteSpace(resolvedFilePath) && File.Exists(resolvedFilePath))
            return resolvedFilePath;

        var matchingFile = FindExistingTrackFile(baseName);
        if (matchingFile != null)
            return matchingFile;

        throw new FileNotFoundException("Downloaded audio file could not be located.", outputTemplate);
    }

    private async Task<TrackDownloadResult> DownloadWithYoutubeExplodeAsync(
        Track track,
        string baseName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = await _youtube.Videos.Streams.GetManifestAsync(track.Id, cancellationToken);
        var stream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate()
            ?? throw new InvalidOperationException("No downloadable audio stream was found.");

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

    private string? FindExistingTrackFile(string baseName)
    {
        if (!Directory.Exists(_downloadDirectory))
            return null;

        var files = Directory.GetFiles(_downloadDirectory, $"{baseName}.*");
        return files.FirstOrDefault(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                                         !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                                         !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "music" : sanitized;
    }
}
