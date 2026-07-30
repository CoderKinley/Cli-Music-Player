using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.Infrastructure;

public sealed class MpvAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly IMusicSource _musicSource;
    private readonly ChildProcessLifetime _childProcesses = new();
    private Process? _process;
    private NamedPipeClientStream? _ipcPipe;
    private StreamWriter? _ipcWriter;

    public MpvAudioPlayer(IMusicSource musicSource) => _musicSource = musicSource;

    public Track? CurrentTrack { get; private set; }
    public bool IsPlaying => _process is { HasExited: false };
    public bool IsPaused { get; private set; }
    public int Volume { get; private set; } = 50;
    public TimeSpan Elapsed =>
        IsPlaying && _startedAt is not null
            ? (_pausedAt ?? DateTimeOffset.Now) - _startedAt.Value - _pausedDuration
            : TimeSpan.Zero;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _pausedAt;
    private TimeSpan _pausedDuration;

    public async Task PlayAsync(
        Track track,
        TimeSpan? startPosition = null,
        CancellationToken cancellationToken = default)
    {
        Stop();

        string source;
        try
        {
            source = await _musicSource.GetPlayableSourceAsync(track, cancellationToken);
        }
        catch
        {
            source = $"https://www.youtube.com/watch?v={track.Id}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "mpv",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var pipeName = $"kmusicplayer-{Environment.ProcessId}-{Guid.NewGuid():N}";
        startInfo.ArgumentList.Add("--no-video");
        startInfo.ArgumentList.Add("--force-window=no");
        startInfo.ArgumentList.Add("--really-quiet");
        startInfo.ArgumentList.Add($"--input-ipc-server=\\\\.\\pipe\\{pipeName}");
        startInfo.ArgumentList.Add($"--volume={Volume}");
        if (startPosition is { TotalSeconds: > 0 })
            startInfo.ArgumentList.Add($"--start={startPosition.Value.TotalSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture)}");
        startInfo.ArgumentList.Add($"--title={track.Title}");
        startInfo.ArgumentList.Add(source);

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("mpv could not be started.");
        _childProcesses.Track(_process);
        await ConnectIpcAsync(pipeName, cancellationToken);
        CurrentTrack = track;
        _startedAt = DateTimeOffset.Now - (startPosition ?? TimeSpan.Zero);
        _pausedAt = null;
        _pausedDuration = TimeSpan.Zero;
        IsPaused = false;
    }

    public void ChangeVolume(int delta)
        => SetVolume(Volume + delta);

    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);

        if (_process is not { HasExited: false })
            return;

        try
        {
            SendCommand("set_property", "volume", Volume);
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void TogglePause()
    {
        if (!IsPlaying || _ipcWriter is null)
            return;

        try
        {
            SendCommand("cycle", "pause");
            IsPaused = !IsPaused;
            if (IsPaused)
            {
                _pausedAt = DateTimeOffset.Now;
            }
            else if (_pausedAt is not null)
            {
                _pausedDuration += DateTimeOffset.Now - _pausedAt.Value;
                _pausedAt = null;
            }
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Seek(TimeSpan offset)
    {
        if (!IsPlaying || _ipcWriter is null || _startedAt is null)
            return;

        try
        {
            SendCommand("seek", offset.TotalSeconds, "relative");

            var target = Elapsed + offset;
            if (target < TimeSpan.Zero)
                target = TimeSpan.Zero;
            if (CurrentTrack?.Duration is { } duration && target > duration)
                target = duration;

            var reference = _pausedAt ?? DateTimeOffset.Now;
            _startedAt = reference - _pausedDuration - target;
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Stop()
    {
        _ipcWriter?.Dispose();
        _ipcWriter = null;
        _ipcPipe?.Dispose();
        _ipcPipe = null;

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        CurrentTrack = null;
        _startedAt = null;
        _pausedAt = null;
        _pausedDuration = TimeSpan.Zero;
        IsPaused = false;
    }

    private void SendCommand(params object[] values)
    {
        if (_ipcWriter is null)
            return;

        _ipcWriter.WriteLine(JsonSerializer.Serialize(new { command = values }));
        _ipcWriter.Flush();
    }

    private async Task ConnectIpcAsync(string pipeName, CancellationToken cancellationToken)
    {
        _ipcPipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await _ipcPipe.ConnectAsync(3000, cancellationToken);
            _ipcWriter = new StreamWriter(_ipcPipe) { AutoFlush = true };
        }
        catch (TimeoutException)
        {
            _ipcPipe.Dispose();
            _ipcPipe = null;
        }
        catch (IOException)
        {
            _ipcPipe.Dispose();
            _ipcPipe = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _childProcesses.Dispose();
    }
}
