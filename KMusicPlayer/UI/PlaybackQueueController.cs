using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.UI;

/// <summary>
/// Owns queue order and navigation independently from terminal rendering.
/// </summary>
public sealed class PlaybackQueueController
{
    private readonly MusicApplication _music;
    private IReadOnlyList<Track> _tracks = [];
    private int _index = -1;
    private bool _loop;
    private string? _sourceId;
    private bool _currentRemoved;

    public PlaybackQueueController(MusicApplication music) => _music = music;

    public bool Shuffle { get; private set; }
    public bool HasTracks => _tracks.Count > 0;
    public bool HasFinishedCurrent =>
        HasTracks && _music.CurrentTrack is not null && !_music.IsPlaying;

    public async Task<Track> StartAsync(
        IReadOnlyList<Track> tracks,
        int selectedIndex,
        bool loop,
        string? sourceId = null)
    {
        if (tracks.Count == 0)
            throw new InvalidOperationException("Cannot play an empty queue.");

        _tracks = tracks.ToList();
        _index = Math.Clamp(selectedIndex, 0, _tracks.Count - 1);
        _loop = loop;
        _sourceId = sourceId;
        return await PlayCurrentAsync();
    }

    public async Task<Track?> NextAsync()
    {
        if (!HasTracks)
            return null;

        if (Shuffle && _index < 0)
        {
            _index = Random.Shared.Next(_tracks.Count);
        }
        else if (Shuffle && _tracks.Count > 1)
        {
            var next = Random.Shared.Next(_tracks.Count - 1);
            _index = next >= _index ? next + 1 : next;
        }
        else if (_index + 1 < _tracks.Count)
        {
            _index++;
        }
        else if (_loop)
        {
            _index = 0;
        }
        else
        {
            Clear();
            return null;
        }

        return await PlayCurrentAsync();
    }

    public async Task<Track?> PreviousAsync()
    {
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

    public bool ToggleShuffle() => Shuffle = !Shuffle;
    public void SetShuffle(bool enabled) => Shuffle = enabled;

    public void RemoveTrack(string sourceId, int trackIndex)
    {
        if (_sourceId != sourceId || trackIndex < 0 || trackIndex >= _tracks.Count)
            return;

        var tracks = _tracks.ToList();
        tracks.RemoveAt(trackIndex);
        _tracks = tracks;
        if (trackIndex == _index)
            _currentRemoved = true;
        if (trackIndex <= _index)
            _index--;
        if (tracks.Count == 0)
            Clear();
    }

    public void Stop()
    {
        _music.Stop();
        Clear();
    }

    private async Task<Track> PlayCurrentAsync()
    {
        var track = _tracks[_index];
        await _music.PlayAsync(track);
        _currentRemoved = false;
        return track;
    }

    private void Clear()
    {
        _tracks = [];
        _index = -1;
        _sourceId = null;
        _currentRemoved = false;
    }
}
