using KMusicPlayer.Application;
using KMusicPlayer.Domain;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace KMusicPlayer.Infrastructure;

public sealed class YouTubeMusicSource : IMusicSource
{
    private readonly YoutubeClient _youtube = new();

    public async Task<IReadOnlyList<Track>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var videos = await _youtube.Search
            .GetVideosAsync(query, cancellationToken)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return videos
            .Select(video => new Track(
                video.Id.Value,
                video.Title,
                video.Author.ChannelTitle,
                video.Duration))
            .ToList();
    }

    public async Task<string> GetPlayableSourceAsync(
        Track track,
        CancellationToken cancellationToken = default)
    {
        if (LocalTrack.TryGetPath(track.Id, out var localPath))
            return File.Exists(localPath)
                ? localPath
                : throw new FileNotFoundException("The local audio file no longer exists.", localPath);

        var manifest = await _youtube.Videos.Streams
            .GetManifestAsync(track.Id, cancellationToken);
        var stream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

        return stream?.Url
            ?? throw new InvalidOperationException("No playable audio stream was found.");
    }

    public async Task<PlaylistResult> GetPlaylistAsync(
        string playlistUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(playlistUrl.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Enter a valid YouTube playlist URL.");
        }

        var playlist = await _youtube.Playlists.GetAsync(playlistUrl, cancellationToken);
        var videos = await _youtube.Playlists
            .GetVideosAsync(playlistUrl, cancellationToken)
            .ToListAsync(cancellationToken);
        var tracks = videos.Select(video => new Track(
            video.Id.Value,
            video.Title,
            video.Author.ChannelTitle,
            video.Duration)).ToList();
        return new PlaylistResult(playlist.Title, tracks);
    }
}
