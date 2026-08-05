using KMusicPlayer.Domain;

namespace KMusicPlayer.Application;

public interface IMusicSource
{
    Task<IReadOnlyList<Track>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);

    Task<string> GetPlayableSourceAsync(
        Track track,
        CancellationToken cancellationToken = default);

    Task<PlaylistResult> GetPlaylistAsync(
        string playlistUrl,
        CancellationToken cancellationToken = default);
}

public sealed record PlaylistResult(string Title, IReadOnlyList<Track> Tracks);

public interface IPlaylistRepository
{
    Task<IReadOnlyList<SavedPlaylist>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SavedPlaylist playlist, CancellationToken cancellationToken = default);
    Task DeleteAsync(string playlistId, CancellationToken cancellationToken = default);
}

public sealed record SavedPlaylist(
    string Id,
    string Name,
    string? SourceUrl,
    IReadOnlyList<Track> Tracks);

public interface IFavoriteRepository
{
    Task<IReadOnlyList<Track>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> ContainsAsync(string trackId, CancellationToken cancellationToken = default);
    Task AddAsync(Track track, CancellationToken cancellationToken = default);
    Task RemoveAsync(string trackId, CancellationToken cancellationToken = default);
}

public interface IHistoryRepository
{
    Task<IReadOnlyList<Track>> GetRecentAsync(CancellationToken cancellationToken = default);
    Task SaveRecentAsync(IReadOnlyList<Track> tracks, CancellationToken cancellationToken = default);
}

public interface ISettingsRepository
{
    Task<int> GetVolumeAsync(CancellationToken cancellationToken = default);
    Task SaveVolumeAsync(int volume, CancellationToken cancellationToken = default);
    Task<bool> GetShuffleAsync(CancellationToken cancellationToken = default);
    Task SaveShuffleAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<string> GetThemeAsync(CancellationToken cancellationToken = default);
    Task SaveThemeAsync(string theme, CancellationToken cancellationToken = default);
}

public interface IPlaybackSessionRepository
{
    Task<PlaybackSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PlaybackSession session, CancellationToken cancellationToken = default);
}

public sealed record PlaybackSession(Track Track, double PositionSeconds);

public interface ILyricsService
{
    Task<LyricsResult?> GetAsync(Track track, CancellationToken cancellationToken = default);
    Task SaveManualAsync(Track track, string lyrics, CancellationToken cancellationToken = default);
}

public sealed record LyricsResult(string Text, bool IsManual);

public interface ITrackDownloadService
{
    Task<TrackDownloadResult> DownloadAsync(
        Track track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record TrackDownloadResult(string FilePath, bool AlreadyExisted);

public interface ILocalMusicLibrary
{
    string DirectoryPath { get; }
    Task<IReadOnlyList<Track>> ScanAsync(CancellationToken cancellationToken = default);
    Task SetDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
}

public interface ISettingsTransferService
{
    Task<string> ExportAsync(CancellationToken cancellationToken = default);
    Task ImportAsync(string backupPath, CancellationToken cancellationToken = default);
}

public interface IAudioPlayer
{
    Track? CurrentTrack { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    int Volume { get; }
    TimeSpan Elapsed { get; }
    Task PlayAsync(
        Track track,
        TimeSpan? startPosition = null,
        CancellationToken cancellationToken = default);
    void ChangeVolume(int delta);
    void SetVolume(int volume);
    void TogglePause();
    void Seek(TimeSpan offset);
    void Stop();
}
