using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.UI;

/// <summary>
/// Owns queue order and navigation independently from terminal rendering.
/// </summary>
public sealed class PlaybackQueueController : IDisposable, IAsyncDisposable
{
    private readonly MusicApplication _music;
    private IReadOnlyList<Track> _tracks = [];
    private int _index = -1;
    private bool _loop;
    private string? _sourceId;
    private bool _currentRemoved;
    private int? _nextIndex;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _automatic;
    public event Action<string>? StatusChanged;

    public PlaybackQueueController(MusicApplication music) => _music = music;

    public bool Shuffle { get; private set; }
    public bool HasTracks => _tracks.Count > 0;
    public bool HasFinishedCurrent =>
        _automatic is null && HasTracks && _music.CurrentTrack is not null && !_music.IsPlaying;

    public void StartAutomaticPlayback() => _automatic ??= RunAutomaticAsync(_lifetime.Token);

    private async Task RunAutomaticAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(100, token);
                await _gate.WaitAsync(token);
                try
                {
                    if (!HasTracks || _music.CurrentTrack is null) continue;
                    if (_music.ShouldTransition || !_music.IsPlaying)
                    {
                        var next = await NextCoreAsync(token, automatic: true);
                        if (next is null) _music.Finish();
                        StatusChanged?.Invoke(next is null ? "Queue finished" : $"Playing: {next.Title}");
                    }
                    if (HasTracks && _music.IsPlaying)
                    {
                        _nextIndex ??= ChooseNextIndex();
                        if (_nextIndex >= 0) _ = _music.PrepareAsync(_tracks[_nextIndex.Value], token);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Clear();
                    StatusChanged?.Invoke($"Queue playback failed: {exception.Message}");
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<Track> StartAsync(
        IReadOnlyList<Track> tracks,
        int selectedIndex,
        bool loop,
        string? sourceId = null)
    {
        await _gate.WaitAsync();
        try { return await StartCoreAsync(tracks, selectedIndex, loop, sourceId); }
        catch { Clear(); throw; }
        finally { _gate.Release(); }
    }

    private async Task<Track> StartCoreAsync(IReadOnlyList<Track> tracks, int selectedIndex, bool loop, string? sourceId)
    {
        if (tracks.Count == 0)
            throw new InvalidOperationException("Cannot play an empty queue.");

        _tracks = tracks.ToList();
        _index = Math.Clamp(selectedIndex, 0, _tracks.Count - 1);
        _loop = loop;
        _sourceId = sourceId;
        _nextIndex = null;
        return await PlayCurrentAsync();
    }

    public async Task<Track?> NextAsync()
    {
        await _gate.WaitAsync();
        try { return await NextCoreAsync(); }
        catch { Clear(); throw; }
        finally { _gate.Release(); }
    }

    private int ChooseNextIndex()
    {
        if (!HasTracks) return -1;
        if (Shuffle && _index < 0) return Random.Shared.Next(_tracks.Count);
        if (Shuffle && _tracks.Count > 1)
        {
            var next = Random.Shared.Next(_tracks.Count - 1);
            return next >= _index ? next + 1 : next;
        }
        return _index + 1 < _tracks.Count ? _index + 1 : _loop ? 0 : -1;
    }

    private async Task<Track?> NextCoreAsync(CancellationToken cancellationToken = default, bool automatic = false)
    {
        if (!HasTracks)
            return null;
        var nextIndex = _nextIndex ?? ChooseNextIndex();
        _nextIndex = null;
        if (nextIndex < 0)
        {
            Clear();
            return null;
        }
        _index = nextIndex;
        return await PlayCurrentAsync(cancellationToken, automatic);
    }

    public async Task<Track?> PreviousAsync()
    {
        await _gate.WaitAsync();
        try { return await PreviousCoreAsync(); }
        catch { Clear(); throw; }
        finally { _gate.Release(); }
    }

    private async Task<Track?> PreviousCoreAsync()
    {
        _nextIndex = null;
        if (!HasTracks)
            return null;

        if (Shuffle && _index < 0)
        {
            _index = Random.Shared.Next(_tracks.Count);
        }
        else if (_currentRemoved && !Shuffle)
        {
            _index = _index < 0 && _loop ? _tracks.Count - 1 : Math.Max(0, _index);
        }
        else if (Shuffle && _tracks.Count > 1)
        {
            var previous = Random.Shared.Next(_tracks.Count - 1);
            _index = previous >= _index ? previous + 1 : previous;
        }
        else if (_index > 0)
        {
            _index--;
        }
        else if (_loop)
        {
            _index = _tracks.Count - 1;
        }
        else
        {
            _index = 0;
        }

        return await PlayCurrentAsync();
    }

    public bool ToggleShuffle() { SetShuffle(!Shuffle); return Shuffle; }
    public void SetShuffle(bool enabled)
    {
        _gate.Wait();
        try { Shuffle = enabled; _nextIndex = null; }
        finally { _gate.Release(); }
    }

    public void RemoveTrack(string sourceId, int trackIndex)
    {
        _gate.Wait();
        try { RemoveTrackCore(sourceId, trackIndex); }
        finally { _gate.Release(); }
    }

    private void RemoveTrackCore(string sourceId, int trackIndex)
    {
        if (_sourceId != sourceId || trackIndex < 0 || trackIndex >= _tracks.Count)
            return;

        var tracks = _tracks.ToList();
        tracks.RemoveAt(trackIndex);
        _tracks = tracks;
        _nextIndex = null;
        if (trackIndex == _index)
            _currentRemoved = true;
        if (trackIndex <= _index)
            _index--;
        if (tracks.Count == 0)
            Clear();
    }

    public void Stop()
    {
        _gate.Wait();
        try { _music.Stop(); Clear(); }
        finally { _gate.Release(); }
    }

    private async Task<Track> PlayCurrentAsync(CancellationToken cancellationToken = default, bool automatic = false)
    {
        var track = _tracks[_index];
        await _music.PlayAsync(track, cancellationToken, automatic);
        _currentRemoved = false;
        _nextIndex = ChooseNextIndex();
        if (_nextIndex >= 0) _ = _music.PrepareAsync(_tracks[_nextIndex.Value], _lifetime.Token);
        return track;
    }

    private void Clear()
    {
        _tracks = [];
        _index = -1;
        _sourceId = null;
        _currentRemoved = false;
        _nextIndex = null;
    }

    public void Dispose() => _lifetime.Cancel();
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_automatic is not null) await _automatic;
    }
}
