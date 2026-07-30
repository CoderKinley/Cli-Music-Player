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
        var manifest = await _youtube.Videos.Streams
            .GetManifestAsync(track.Id, cancellationToken);
        var stream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

        return stream?.Url
            ?? throw new InvalidOperationException("No playable audio stream was found.");
    }
}
