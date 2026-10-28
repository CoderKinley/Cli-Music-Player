using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.Infrastructure;

public sealed class MpvAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly IMusicSource _musicSource;
    private readonly ChildProcessLifetime _childProcesses = new();
    private readonly object _sync = new();
    private Voice? _current;
    private Voice? _outgoing;
    private readonly PreloadSlot _queued = new();
    private readonly PreloadSlot _selection = new();
    private CancellationTokenSource _fade = new();
    private AudioPreferences _preferences = new();
    private Track? _track;
    private bool _disposed;
    private readonly ProcessAudioSpectrum? _outputSpectrum;
    private long _silenceScansStarted;
    private CancellationTokenSource? _silenceScanCancellation;
    private Task _silenceScanWork = Task.CompletedTask;
    public long SilenceScansStarted => Interlocked.Read(ref _silenceScansStarted);

    public MpvAudioPlayer(IMusicSource musicSource)
    {
        _musicSource = musicSource;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) _outputSpectrum = new ProcessAudioSpectrum();
    }
    public string SpectrumBackend => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) && _outputSpectrum?.IsRunning == true
        ? "WASAPI FFT" : "Filter bank";
    public string SpectrumDiagnostics => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) && _outputSpectrum is { } output
        ? $"packets={output.Packets} FFTs={output.FramesPublished} peak={output.PeakSample:0.00000000} discontinuities={output.Discontinuities} error={output.Failure}" : "unavailable";
    public Track? CurrentTrack => _track;
    public bool IsPlaying => _current?.IsAlive == true;
    public bool IsPaused => _current?.Paused == true;
    public int Volume { get; private set; } = 50;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(Math.Max(0, _current?.Position ?? 0));
    private double End(Voice voice) => _preferences.TrimSilence && voice.TrimmedEnd > 0 ? voice.TrimmedEnd : voice.Duration;
    public IReadOnlyList<double> Spectrum
    {
        get
        {
            var current = _current;
            if (current is null || !current.IsAlive || current.Paused || Volume == 0)
                return Array.Empty<double>();
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) && _outputSpectrum?.IsRunning == true)
                return _outputSpectrum.Levels;
            var outgoing = _outgoing;
            var first = current.Levels;
            var second = outgoing?.Levels;
            var mixed = new double[AudioAnalysis.Frequencies.Length];
            for (var i = 0; i < mixed.Length; i++)
                mixed[i] = Math.Clamp(first[i] * current.Gain + (second?[i] ?? 0) * (outgoing?.Gain ?? 0), 0, 1);
            return mixed;
        }
    }

    public bool ShouldTransition
    {
        get
        {
            var voice = _current;
            if (voice is null || voice.Paused || !voice.Ready.Task.IsCompletedSuccessfully)
                return false;
            var end = End(voice);
            var fade = Math.Min(_preferences.CrossfadeSeconds, Math.Max(0, end - voice.PlaybackStart) * 0.25);
            return end > 0 && voice.Position > 0 && end - voice.Position <= fade;
        }
    }

    public void Configure(AudioPreferences preferences)
    {
        var trimChanged = _preferences.TrimSilence != preferences.TrimSilence;
        _preferences = preferences.Normalize();
        if (trimChanged)
        {
            lock (_sync)
            {
                CancelPreparation(deferDisposal: true);
                CancelSilenceScan();
                if (_current is { IsAlive: true } current) StartSilenceScan(current);
            }
        }
    }

    public Task PrepareAsync(Track track, CancellationToken cancellationToken = default) =>
        PrepareInSlot(_queued, track, false, cancellationToken);

    public Task WarmSelectionAsync(Track track, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_disposed || _current?.Track.Id == track.Id) return Task.CompletedTask;
            if (_queued.TrackId == track.Id && _queued.Work is not null) return _queued.Work;
            return PrepareInSlot(_selection, track, true, cancellationToken);
        }
    }

    private Task PrepareInSlot(PreloadSlot slot, Track track, bool debounce, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_disposed || slot.Voice?.Track.Id == track.Id) return Task.CompletedTask;
            if (slot.TrackId == track.Id && slot.Work is not null) return slot.Work;
            slot.Reset(deferDisposal: true);
            slot.TrackId = track.Id;
            slot.Work = PrepareCoreAsync(slot, track, debounce, slot.Cancellation.Token, cancellationToken);
            return slot.Work;
        }
    }

    private async Task PrepareCoreAsync(PreloadSlot slot, Track track, bool debounce, CancellationToken token, CancellationToken callerToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, callerToken);
        Voice? voice = null;
        try
        {
            if (debounce)
                await Task.WhenAny(Task.Delay(180, linked.Token), slot.PlayRequested.Task);
            linked.Token.ThrowIfCancellationRequested();
            voice = await CreateVoiceAsync(track, null, linked.Token);
            await Task.WhenAny(voice.OutputReady.Task, slot.PlayRequested.Task, Task.Delay(500, linked.Token));
            linked.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                slot.Voice = voice;
                voice = null;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Failed preloads never interrupt the current song; normal playback can retry.
        }
        finally { voice?.Dispose(); }
    }

    public Task PlayAsync(Track track, TimeSpan? startPosition = null, CancellationToken cancellationToken = default) =>
        PlayCoreAsync(track, startPosition, false, cancellationToken);

    public Task TransitionAsync(Track track, bool automatic, CancellationToken cancellationToken = default) =>
        PlayCoreAsync(track, null, automatic, cancellationToken);

    private async Task PlayCoreAsync(Track track, TimeSpan? startPosition, bool automatic, CancellationToken cancellationToken)
    {
        Voice? next;
        Task? preparing;
        lock (_sync)
        {
            var slot = _selection.TrackId == track.Id ? _selection : _queued.TrackId == track.Id ? _queued : null;
            slot?.PlayRequested.TrySetResult(); // Bypass the hover debounce when Enter is pressed.
            preparing = startPosition is null ? slot?.Work : null;
        }
        if (preparing is not null) await preparing.WaitAsync(cancellationToken);
        lock (_sync)
        {
            var slot = _selection.Voice?.Track.Id == track.Id ? _selection : _queued.Voice?.Track.Id == track.Id ? _queued : null;
            next = startPosition is null && slot?.Voice is { IsAlive: true } prepared ? prepared : null;
            if (next is not null) slot!.Voice = null;
            CancelPreparation(deferDisposal: true);
        }
        next ??= await CreateVoiceAsync(track, startPosition, cancellationToken);
        var previous = _current;
        var seconds = _preferences.CrossfadeSeconds;
        var nextEnd = End(next);
        if (nextEnd > 0) seconds = Math.Min(seconds, Math.Max(0, nextEnd - next.Position) * 0.25);
        if (previous is { IsAlive: true })
        {
            var previousEnd = End(previous);
            if (previousEnd > 0) seconds = Math.Min(seconds, Math.Max(0, previousEnd - previous.Position));
        }
        if (previous?.Paused == true || previous?.IsAlive != true) seconds = seconds > 0 ? 0.25 : 0;
        var incomingSeconds = automatic ? seconds : 0;
        try
        {
            next.ApplyFade(true, incomingSeconds);
            next.SetGain(1, Volume);
            next.Paused = false;
            next.Send("set_property", "pause", false);
            await next.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch { next.Dispose(); throw; }
        _fade.Cancel();
        _fade.Dispose();
        _fade = new CancellationTokenSource();
        _outgoing?.Dispose();
        _outgoing = previous;
        next.PlaybackStart = next.Position;
        _current = next;
        _track = track;
        StartSilenceScan(next);
        if (previous?.Paused == true || previous?.IsAlive != true)
        {
            previous?.Dispose();
            _outgoing = null;
        }
        _outgoing?.ApplyFade(false, seconds, _outgoing.Gain);
        _ = FadeAsync(next, _outgoing, seconds, incomingSeconds, _fade.Token);
    }

    private async Task FadeAsync(Voice incoming, Voice? outgoing, double seconds, double incomingSeconds, CancellationToken token)
    {
        var elapsed = 0d;
        var clock = Stopwatch.StartNew();
        var previousTime = clock.Elapsed.TotalSeconds;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var now = clock.Elapsed.TotalSeconds;
                if (!incoming.Paused) elapsed += now - previousTime;
                previousTime = now;
                var progress = seconds <= 0 ? 1 : Math.Clamp(elapsed / seconds, 0, 1);
                var gains = AudioAnalysis.FadeGains(progress);
                incoming.Gain = AudioAnalysis.FadeGains(incomingSeconds <= 0 ? 1 : elapsed / incomingSeconds).Incoming;
                if (outgoing is not null) outgoing.Gain = outgoing.FadeUnity * gains.Outgoing;
                if (progress >= 1) break;
                await Task.Delay(10, token);
            }
            outgoing?.Dispose();
            if (ReferenceEquals(_outgoing, outgoing)) _outgoing = null;
        }
        catch (OperationCanceledException) { }
    }

    public void ChangeVolume(int delta) => SetVolume(Volume + delta);
    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        var current = _current;
        var outgoing = _outgoing;
        current?.Send("set_property", "volume", Volume);
        outgoing?.Send("set_property", "volume", Volume);
    }

    public void TogglePause()
    {
        if (_current is not { IsAlive: true } current) return;
        var paused = !current.Paused;
        current.Paused = paused;
        current.Send("set_property", "pause", paused);
        if (_outgoing is { } outgoing)
        {
            outgoing.Paused = paused;
            outgoing.Send("set_property", "pause", paused);
        }
    }

    public void Seek(TimeSpan offset)
    {
        if (_current is not { IsAlive: true } current) return;
        _fade.Cancel();
        _outgoing?.Dispose();
        _outgoing = null;
        current.ClearFade();
        current.SetGain(1, Volume);
        current.Send("seek", offset.TotalSeconds, "relative+exact");
    }

    public void Stop()
    {
        _fade.Cancel();
        lock (_sync)
        {
            CancelSilenceScan();
            CancelPreparation();
        }
        _outgoing?.Dispose();
        _outgoing = null;
        _current?.Dispose();
        _current = null;
        _track = null;
    }

    public void Finish()
    {
        if (_current is not { } voice) return;
        _fade.Cancel();
        _fade.Dispose();
        _fade = new CancellationTokenSource();
        lock (_sync)
        {
            CancelSilenceScan();
            CancelPreparation();
        }
        _ = FinishCoreAsync(voice, _fade.Token);
    }

    private async Task FinishCoreAsync(Voice voice, CancellationToken token)
    {
        var end = End(voice);
        var seconds = Math.Clamp(end - voice.Position, 0, _preferences.CrossfadeSeconds);
        voice.ApplyFade(false, seconds, voice.Gain);
        var elapsed = 0d;
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalSeconds;
        try
        {
            while (seconds > 0 && elapsed < seconds)
            {
                var now = clock.Elapsed.TotalSeconds;
                if (!voice.Paused) elapsed += now - previous;
                previous = now;
                token.ThrowIfCancellationRequested();
                voice.Gain = voice.FadeUnity * Math.Cos(Math.Clamp(elapsed / seconds, 0, 1) * Math.PI / 2);
                await Task.Delay(10, token);
            }
            token.ThrowIfCancellationRequested();
            voice.Dispose();
        }
        catch (OperationCanceledException) { }
    }

    private void CancelPreparation(bool deferDisposal = false)
    {
        _queued.Reset(deferDisposal);
        _selection.Reset(deferDisposal);
    }

    private async Task<Voice> CreateVoiceAsync(Track track, TimeSpan? startPosition, CancellationToken token)
    {
        string source;
        try { source = await _musicSource.GetPlayableSourceAsync(track, token); }
        catch (Exception exception) when (exception is not OperationCanceledException && !track.IsLocal)
        { source = $"https://www.youtube.com/watch?v={track.Id}"; }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pipeName = $"kmusicplayer-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var info = CreateStartInfo(source);
        info.ArgumentList.Add($"--input-ipc-server=\\\\.\\pipe\\{pipeName}");
        info.ArgumentList.Add("--pause=yes");
        info.ArgumentList.Add("--volume=0");
        info.ArgumentList.Add("--audio-buffer=0.06");
        info.ArgumentList.Add("--cache-pause-initial=no");
        info.ArgumentList.Add("--demuxer-readahead-secs=10");
        info.ArgumentList.Add($"--af=@kmusic:lavfi=[{AudioAnalysis.CreateFilter(_preferences.TrimSilence && startPosition is not { TotalSeconds: > 0 })}]");
        if (startPosition is { TotalSeconds: > 0 }) info.ArgumentList.Add($"--start={Number(startPosition.Value.TotalSeconds)}");
        var voice = new Voice(track, source);
        try
        {
            voice.Process = Process.Start(info) ?? throw new InvalidOperationException("mpv could not be started.");
            _childProcesses.Track(voice.Process);
            voice.ReadOutput();
            await voice.ConnectAsync(pipeName, token);
            await voice.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            return voice;
        }
        catch { voice.Dispose(); throw; }
    }

    private static ProcessStartInfo CreateStartInfo(string source)
    {
        var info = new ProcessStartInfo
        {
            FileName = ToolResolver.ResolveMpvExecutable(), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (var arg in new[] { "--no-config", "--no-video", "--force-window=no", "--really-quiet", "--ytdl-format=bestaudio/best" })
            info.ArgumentList.Add(arg);
        var ytdl = ToolResolver.ResolveYtDlpExecutable();
        if (!string.IsNullOrWhiteSpace(ytdl)) info.ArgumentList.Add($"--script-opts=ytdl_hook-ytdl_path={ytdl}");
        if (ToolResolver.HasNodeJs()) info.ArgumentList.Add("--ytdl-raw-options=js-runtimes=node");
        info.ArgumentList.Add(source);
        return info;
    }

    private void StartSilenceScan(Voice voice)
    {
        lock (_sync)
        {
            CancelSilenceScan();
            if (!_preferences.TrimSilence || voice.Duration <= 1) return;
            _silenceScanCancellation = CancellationTokenSource.CreateLinkedTokenSource(voice.Lifetime.Token);
            var token = _silenceScanCancellation.Token;
            var previous = _silenceScanWork;
            // Only the playing song needs a tail scan. Wait for obsolete scan cleanup
            // before starting another decoder, without holding up playback or navigation.
            _silenceScanWork = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                if (!token.IsCancellationRequested) await ProbeTailAsync(voice, token).ConfigureAwait(false);
            });
        }
    }

    // Called under _sync; cancellation callbacks and process cleanup never run on the UI thread.
    private void CancelSilenceScan()
    {
        var cancellation = _silenceScanCancellation;
        _silenceScanCancellation = null;
        if (cancellation is not null) _ = CancelAndDisposeAsync(cancellation);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        finally { cancellation.Dispose(); }
    }

    private async Task ProbeTailAsync(Voice voice, CancellationToken token)
    {
        Process? probe = null;
        try
        {
            var duration = voice.Duration;
            if (duration <= 1) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var info = CreateStartInfo(voice.Source);
            info.ArgumentList.Add("--ao=null");
            info.ArgumentList.Add("--ao-null-untimed=yes");
            info.ArgumentList.Add("--untimed");
            info.ArgumentList.Add("--cache=no");
            info.ArgumentList.Add("--demuxer-readahead-secs=0");
            info.ArgumentList.Add($"--start={Number(Math.Max(0, duration - 30))}");
            info.ArgumentList.Add("--af=lavfi=[silencedetect=noise=-55dB:d=0.15,ametadata=print:file=-:direct=1]");
            timeout.Token.ThrowIfCancellationRequested();
            probe = Process.Start(info);
            if (probe is null) return;
            Interlocked.Increment(ref _silenceScansStarted);
            _childProcesses.Track(probe);
            var errors = probe.StandardError.ReadToEndAsync(timeout.Token);
            double? silenceStart = null;
            while (await probe.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.StartsWith("lavfi.silence_start=", StringComparison.Ordinal)
                    && double.TryParse(line.AsSpan("lavfi.silence_start=".Length), CultureInfo.InvariantCulture, out var start)) silenceStart = start;
                else if (line.StartsWith("lavfi.silence_end=", StringComparison.Ordinal)) silenceStart = null;
            }
            await probe.WaitForExitAsync(timeout.Token);
            await errors;
            token.ThrowIfCancellationRequested();
            if (probe.ExitCode == 0 && silenceStart is > 0 && silenceStart < duration - 0.15)
                voice.TrimmedEnd = Math.Max(0.1, silenceStart.Value - 0.025);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        finally
        {
            if (probe is not null)
            {
                try { if (!probe.HasExited) probe.Kill(entireProcessTree: true); }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                probe.Dispose();
            }
        }
    }

    private static string Number(double number) => number.ToString(CultureInfo.InvariantCulture);
    public void Dispose()
    {
        _disposed = true;
        Stop();
        _queued.Cancellation.Dispose();
        _selection.Cancellation.Dispose();
        _fade.Dispose();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) _outputSpectrum?.Dispose();
        _childProcesses.Dispose();
    }

    private sealed class PreloadSlot
    {
        public string? TrackId;
        public Voice? Voice;
        public Task? Work;
        public CancellationTokenSource Cancellation = new();
        public TaskCompletionSource PlayRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset(bool deferDisposal = false)
        {
            var cancellation = Cancellation;
            var voice = Voice;
            Cancellation = new CancellationTokenSource();
            Voice = null;
            Work = null;
            TrackId = null;
            PlayRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (deferDisposal)
            {
                // CancelAsync marks the old token immediately, keeping stale work from
                // publishing into this slot. Callbacks and process cleanup stay off the UI thread.
                var cancelled = cancellation.CancelAsync();
                _ = Task.Run(async () =>
                {
                    try { await cancelled.ConfigureAwait(false); }
                    finally { cancellation.Dispose(); voice?.Dispose(); }
                });
            }
            else
            {
                try { cancellation.Cancel(); }
                finally { cancellation.Dispose(); voice?.Dispose(); }
            }
        }
    }

    private sealed class Voice(Track track, string source) : IDisposable
    {
        private readonly object _writeLock = new();
        private NamedPipeClientStream? _pipe;
        private StreamWriter? _writer;
        private int _disposed;
        public Track Track { get; } = track;
        public string Source { get; } = source;
        public Process? Process { get; set; }
        public CancellationTokenSource Lifetime { get; } = new();
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OutputReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public double Position;
        public double DecodedPosition;
        public double PlaybackStart;
        public double Duration = track.Duration?.TotalSeconds ?? 0;
        public double TrimmedEnd;
        public volatile bool Paused = true;
        public double Gain;
        public double FadeUnity = 1;
        private bool _hasFade;
        public double[] Levels = new double[AudioAnalysis.Frequencies.Length];
        public bool IsAlive
        {
            get { try { return _disposed == 0 && Process is { HasExited: false }; } catch (InvalidOperationException) { return false; } }
        }

        public async Task ConnectAsync(string pipeName, CancellationToken token)
        {
            _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(5000, token);
            _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
            _ = ReadIpcAsync();
            Send("observe_property", 1, "time-pos");
            Send("observe_property", 2, "duration");
            Send("observe_property", 3, "audio-out-params");
            Send("get_property", "duration");
        }

        public void SetGain(double gain, int volume)
        {
            Gain = gain;
            Send("set_property", "volume", gain * volume);
        }

        public void ApplyFade(bool incoming, double seconds, double unity = 1)
        {
            FadeUnity = unity;
            if (seconds <= 0) { ClearFade(); return; }
            // FFmpeg interpolates the gain for every audio sample; IPC only installs the envelope once.
            var start = incoming ? Math.Max(Position, DecodedPosition) : Position;
            Send("af", "add", $"@transition:lavfi=[afade=t={(incoming ? "in" : "out")}:st={Number(start)}:d={Number(seconds)}:curve=qsin:unity={Number(unity)}]");
            _hasFade = true;
        }

        public void ClearFade()
        {
            if (_hasFade) Send("af", "remove", "@transition");
            _hasFade = false;
        }

        public void Send(params object[] values)
        {
            lock (_writeLock)
            {
                try { _writer?.WriteLine(JsonSerializer.Serialize(new { command = values })); }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException) { }
            }
        }

        public void ReadOutput() { _ = ReadBandsAsync(); _ = DrainErrorsAsync(); }
        private async Task DrainErrorsAsync()
        {
            try { if (Process is not null) await Process.StandardError.ReadToEndAsync(Lifetime.Token); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
        }

        private async Task ReadBandsAsync()
        {
            var frame = new double[AudioAnalysis.Frequencies.Length];
            try
            {
                while (Process is not null && await Process.StandardOutput.ReadLineAsync(Lifetime.Token) is { } line)
                {
                    var timestamp = line.IndexOf("pts_time:", StringComparison.Ordinal);
                    if (timestamp >= 0 && double.TryParse(line.AsSpan(timestamp + 9), CultureInfo.InvariantCulture, out var pts))
                        DecodedPosition = pts;
                    if (AudioAnalysis.TryParseBand(line, out var band, out var level))
                    {
                        frame[band] = level;
                        if (band == frame.Length - 1) { Levels = frame; frame = new double[frame.Length]; }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
        }

        private async Task ReadIpcAsync()
        {
            try
            {
                using var reader = new StreamReader(_pipe!, leaveOpen: true);
                while (await reader.ReadLineAsync(Lifetime.Token) is { } line)
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var eventName = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
                    if (eventName == "file-loaded") Ready.TrySetResult();
                    if (eventName == "playback-restart" && !Paused) Started.TrySetResult();
                    if (eventName == "end-file" && !Started.Task.IsCompleted)
                    {
                        var error = new InvalidOperationException("mpv could not decode this track.");
                        Ready.TrySetException(error);
                        Started.TrySetException(error);
                    }
                    if (!root.TryGetProperty("data", out var data)) continue;
                    var name = root.TryGetProperty("name", out var property) ? property.GetString() : "duration";
                    if (name == "audio-out-params" && data.ValueKind == JsonValueKind.Object) OutputReady.TrySetResult();
                    if (data.ValueKind != JsonValueKind.Number) continue;
                    if (name == "time-pos") { Position = data.GetDouble(); if (!Paused) Started.TrySetResult(); }
                    if (name == "duration") { Duration = data.GetDouble(); Ready.TrySetResult(); }
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or JsonException) { }
            finally
            {
                Ready.TrySetException(new IOException("The mpv connection closed before loading the track."));
                Started.TrySetException(new IOException("The mpv connection closed before playback started."));
                OutputReady.TrySetException(new IOException("The mpv connection closed before audio output was ready."));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Lifetime.Cancel();
            lock (_writeLock) { _writer?.Dispose(); _writer = null; _pipe?.Dispose(); }
            if (Process is not null)
            {
                try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                Process.Dispose();
            }
            Lifetime.Dispose();
        }
    }
}
