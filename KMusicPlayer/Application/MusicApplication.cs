using KMusicPlayer.Domain;

namespace KMusicPlayer.Application;

public sealed class MusicApplication
{
    private readonly IMusicSource _musicSource;
    private readonly IFavoriteRepository _favorites;
    private readonly IAudioPlayer _player;
    private readonly IHistoryRepository _history;
    private readonly ISettingsRepository _settings;
    private readonly IPlaybackSessionRepository _sessionRepository;
    private readonly List<Track> _recentlyPlayed = [];
    private readonly object _historyLock = new();
    private readonly SemaphoreSlim _playbackGate = new(1, 1);

    public MusicApplication(
        IMusicSource musicSource,
        IFavoriteRepository favorites,
        IAudioPlayer player,
        IHistoryRepository history,
        ISettingsRepository settings,
        IPlaybackSessionRepository sessionRepository)
    {
        _musicSource = musicSource;
        _favorites = favorites;
        _player = player;
        _history = history;
        _settings = settings;
        _sessionRepository = sessionRepository;
    }

    public Track? CurrentTrack => _player.CurrentTrack;
    public bool IsPlaying => _player.IsPlaying;
    public bool IsPaused => _player.IsPaused;
    public int Volume => _player.Volume;
    public TimeSpan Elapsed => _player.Elapsed;
    public IReadOnlyList<Track> RecentlyPlayed
    {
        get { lock (_historyLock) return _recentlyPlayed.ToArray(); }
    }
    public PlaybackSession? PreviousSession { get; private set; }
    public bool ShuffleEnabled { get; private set; }
    public string ThemeName { get; private set; } = "Blue";
    public AudioPreferences AudioPreferences { get; private set; } = new();
    public IReadOnlyList<double> Spectrum => _player.Spectrum;
    public bool ShouldTransition => _player.ShouldTransition;
    public Task PrepareAsync(Track track, CancellationToken cancellationToken = default) =>
        _player.PrepareAsync(track, cancellationToken);
    public Task WarmSelectionAsync(Track track, CancellationToken cancellationToken = default) =>
        _player.WarmSelectionAsync(track, cancellationToken);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _player.SetVolume(await _settings.GetVolumeAsync(cancellationToken));
        ShuffleEnabled = await _settings.GetShuffleAsync(cancellationToken);
        ThemeName = await _settings.GetThemeAsync(cancellationToken);
        AudioPreferences = (await _settings.GetAudioPreferencesAsync(cancellationToken)).Normalize();
        _player.Configure(AudioPreferences);
        _recentlyPlayed.Clear();
        _recentlyPlayed.AddRange((await _history.GetRecentAsync(cancellationToken)).Take(10));
        PreviousSession = await _sessionRepository.LoadAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Track>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        _musicSource.SearchAsync(query, limit, cancellationToken);

    public Task<PlaylistResult> GetPlaylistAsync(
        string playlistUrl,
        CancellationToken cancellationToken = default) =>
        _musicSource.GetPlaylistAsync(playlistUrl, cancellationToken);

    public Task<IReadOnlyList<Track>> GetFavoritesAsync(
        CancellationToken cancellationToken = default) =>
        _favorites.GetAllAsync(cancellationToken);

    public async Task PlayAsync(Track track, CancellationToken cancellationToken = default, bool automaticTransition = false)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            await _player.TransitionAsync(track, automaticTransition, cancellationToken);
            Track[] recent;
            lock (_historyLock)
            {
                _recentlyPlayed.RemoveAll(item => item.Id == track.Id);
                _recentlyPlayed.Insert(0, track);
                if (_recentlyPlayed.Count > 10)
                    _recentlyPlayed.RemoveRange(10, _recentlyPlayed.Count - 10);
                recent = _recentlyPlayed.ToArray();
            }
            await _history.SaveRecentAsync(recent, cancellationToken);
            PreviousSession = new PlaybackSession(track, 0);
            await _sessionRepository.SaveAsync(PreviousSession, cancellationToken);
        }
        finally { _playbackGate.Release(); }
    }

    public void Stop() => _player.Stop();
    public void Finish() => _player.Finish();
    public void TogglePause() => _player.TogglePause();
    public void Seek(TimeSpan offset) => _player.Seek(offset);

    public async Task<bool> ResumePreviousSessionAsync(
        CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            if (PreviousSession is null)
                return false;

            var session = PreviousSession;
            await _player.PlayAsync(
                session.Track,
                TimeSpan.FromSeconds(session.PositionSeconds),
                cancellationToken);
            PreviousSession = session;
            await _sessionRepository.SaveAsync(session, cancellationToken);
            return true;
        }
        finally { _playbackGate.Release(); }
    }

    public async Task SavePlaybackSessionAsync(CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            if (_player.CurrentTrack is null)
                return;

            PreviousSession = new PlaybackSession(
                _player.CurrentTrack,
                Math.Max(0, _player.Elapsed.TotalSeconds));
            await _sessionRepository.SaveAsync(PreviousSession, cancellationToken);
        }
        finally { _playbackGate.Release(); }
    }

    public async Task ChangeVolumeAsync(int delta, CancellationToken cancellationToken = default)
    {
        _player.ChangeVolume(delta);
        await _settings.SaveVolumeAsync(_player.Volume, cancellationToken);
    }

    public async Task SetShuffleAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ShuffleEnabled = enabled;
        await _settings.SaveShuffleAsync(enabled, cancellationToken);
    }

    public async Task SetThemeAsync(
        string theme,
        CancellationToken cancellationToken = default)
    {
        ThemeName = theme;
        await _settings.SaveThemeAsync(theme, cancellationToken);
    }

    public async Task SetAudioPreferencesAsync(AudioPreferences preferences, CancellationToken cancellationToken = default)
    {
        AudioPreferences = preferences.Normalize();
        _player.Configure(AudioPreferences);
        await _settings.SaveAudioPreferencesAsync(AudioPreferences, cancellationToken);
    }

    public async Task<bool> ToggleFavoriteAsync(
        Track track,
        CancellationToken cancellationToken = default)
    {
        if (await _favorites.ContainsAsync(track.Id, cancellationToken))
        {
            await _favorites.RemoveAsync(track.Id, cancellationToken);
            return false;
        }

        await _favorites.AddAsync(track, cancellationToken);
        return true;
    }
}
