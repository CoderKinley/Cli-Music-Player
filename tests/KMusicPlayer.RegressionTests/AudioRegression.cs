using System.Diagnostics;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;
using KMusicPlayer.Infrastructure;
using KMusicPlayer.UI;

internal static class AudioRegression
{
    public static async Task RunAsync(Action<bool, string> check, bool integration)
    {
        foreach (var frequency in new[] { 65, 440, 2200, 10000 })
        {
            var analyzer = new FftSpectrumAnalyzer();
            var pcm = new short[9600];
            for (var i = 0; i < pcm.Length / 2; i++)
            {
                pcm[i * 2] = (short)(Math.Sin(i * Math.Tau * frequency / 48000) * 5000);
                pcm[i * 2 + 1] = (short)-pcm[i * 2];
            }
            analyzer.Add(pcm);
            var bins = analyzer.Levels;
            check(Math.Abs(Peak(bins) - FftSpectrumAnalyzer.BandForFrequency(frequency)) <= 1,
                $"FFT misplaced {frequency} Hz or cancelled anti-phase stereo");
            check(bins.Max() > 0.45 && analyzer.FramesPublished >= 9, "FFT response was too slow for a 100 ms transient");
            check(bins.Max() < 0.8, "Normal signal filled the spectrum ceiling instead of leaving transient headroom");
            if (frequency == 2200) check(bins.Take(15).Max() < 0.1, "FFT leaked a high tone into the bass");
            analyzer.Add(new short[12000]);
            check(analyzer.Levels.All(x => x == 0), "Digital silence left stale FFT energy");
        }
        var dynamics = new FftSpectrumAnalyzer();
        float[] Tone(float amplitude) => Enumerable.Range(0, 9600)
            .Select(i => (float)Math.Sin(i / 2 * Math.Tau * 440 / 48000) * amplitude).ToArray();
        dynamics.Add(Tone(0.1f));
        var loud = dynamics.Levels.Max();
        dynamics.Add(Tone(0.01f));
        check(dynamics.Levels.Max() < loud - 0.2, "Automatic sensitivity held the bars high during a quiet passage");
        check(AudioAnalysis.TryParseBand("lavfi.astats.5.RMS_level=-21.1", out var band, out var level)
            && band == 4 && level > 0.75, "Valid frequency energy was not decoded");
        check(AudioAnalysis.TryParseBand("lavfi.astats.1.RMS_level=-inf", out _, out var silent) && silent == 0,
            "Digital silence must have zero energy");
        foreach (var line in new[] { "noise", "lavfi.astats.0.RMS_level=1", "lavfi.astats.13.RMS_level=1", "lavfi.astats.1.RMS_level=NaN" })
            check(!AudioAnalysis.TryParseBand(line, out _, out _), "Malformed spectrum telemetry was accepted");
        for (var i = 0; i <= 100; i++)
        {
            var gains = AudioAnalysis.FadeGains(i / 100d);
            check(Math.Abs(gains.Incoming * gains.Incoming + gains.Outgoing * gains.Outgoing - 1) < 0.000001,
                "Crossfade does not maintain equal power");
        }
        foreach (var style in Enum.GetValues<SpectrumStyle>())
        foreach (var (width, height) in new[] { (0, 0), (1, 1), (34, 3), (60, 12), (160, 30) })
        {
            var quiet = SpectrumPanel.Render(width, height, style, []);
            var active = SpectrumPanel.Render(width, height, style, Enumerable.Repeat(0.8, 12).ToArray());
            check(quiet.Length == height && active.All(row => row.Length == width), "Spectrum exceeded its viewport");
            if (width > 30 && style != SpectrumStyle.Off)
                check(!quiet.SequenceEqual(active), "Spectrum does not respond to frequency measurements");
            if (style == SpectrumStyle.Off)
                check(active.All(row => row.All(c => c == ' ')), "Off left spectrum pixels on screen");
        }
        {
            const int width = 72;
            const int height = 18;
            var quiet = SpectrumPanel.Render(width, height, SpectrumStyle.CatHalo, []);
            var active = SpectrumPanel.Render(width, height, SpectrumStyle.CatHalo,
                Enumerable.Repeat(0.8, 48).ToArray());
            bool Pixel(string[] frame, int x, int y)
            {
                int[] bits = [1, 8, 2, 16, 4, 32, 64, 128];
                var cell = frame[y / 4][x / 2];
                return cell >= 0x2800 && ((cell - 0x2800) & bits[y % 4 * 2 + x % 2]) != 0;
            }
            var cx = (width * 2 - 1) / 2d;
            var cy = (height * 4 - 1) / 2d;
            var radius = Math.Min(width * 2 * 0.29, height * 4 * 0.25);
            check(!Pixel(quiet, (int)Math.Round(cx), (int)Math.Round(cy - radius)),
                "Cat Halo still draws a circle through the forehead");
            check(!Pixel(quiet, (int)Math.Round(cx - radius * 0.61), (int)Math.Round(cy - radius * 1.1))
                && !Pixel(active, (int)Math.Round(cx - radius * 0.61), (int)Math.Round(cy - radius * 1.1)),
                "Cat Halo fills the inside of an ear");
            check(Enumerable.Range((int)Math.Ceiling(cx + radius + 2), (int)(radius * 0.45))
                .Any(x => Enumerable.Range((int)cy - 3, 7).Any(y => Pixel(active, x, y) && !Pixel(quiet, x, y))),
                "Cat Halo audio strokes do not extend outward from the face");
        }
        foreach (var (width, height) in new[] { (34, 3), (60, 12), (90, 20) })
        {
            var wave = SpectrumPanel.Render(width, height, SpectrumStyle.AuroraWave,
                Enumerable.Range(0, 48).Select(i => 0.25 + 0.7 * Math.Pow(Math.Sin(i * 0.4), 2)).ToArray());
            bool Pixel(int x, int y)
            {
                var cell = wave[y / 4][x / 2];
                int[] bits = [1, 8, 2, 16, 4, 32, 64, 128];
                return cell >= 0x2800 && ((cell - 0x2800) & bits[y % 4 * 2 + x % 2]) != 0;
            }
            check(Enumerable.Range(0, height * 2).Any(y => Enumerable.Range(0, width * 2).Any(x => Pixel(x, y))),
                "Aurora Wave did not extend above its center");
            for (var y = 0; y < height * 2; y++)
                check(Enumerable.Range(0, width * 2).All(x => Pixel(x, y) == Pixel(x, height * 4 - 1 - y)),
                    "Aurora Wave is not mirrored above and below the center");
        }

        var directory = Path.Combine(Path.GetTempPath(), "Musik-Audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(settingsPath, "{\"Volume\":65,\"Shuffle\":true,\"Theme\":\"Spotify\"}");
            var settings = new JsonSettingsRepository(settingsPath);
            check(await settings.GetAudioPreferencesAsync() == new AudioPreferences(), "Old settings did not migrate to audio defaults");
            var preferences = new AudioPreferences("AuroraWave", false, 1.5);
            await settings.SaveAudioPreferencesAsync(preferences);
            await settings.SaveVolumeAsync(80);
            await settings.SaveThemeAsync("SoundCloud");
            await settings.SaveShuffleAsync(false);
            check(await settings.GetAudioPreferencesAsync() == preferences, "Other settings updates lost audio preferences");
            check(await settings.GetVolumeAsync() == 80 && await settings.GetThemeAsync() == "SoundCloud",
                "Audio settings changed existing preferences");
            check(new AudioPreferences("Unknown", true, double.NaN).Normalize() == new AudioPreferences(),
                "Invalid audio preferences were not normalized");
            if (!integration) return;

            var first = WriteWave(directory, "Edges and internal pause", 440,
                [(1, false), (2, true), (1, false), (2, true), (2, false)]);
            var second = WriteWave(directory, "High tone", 2200, [(8, true)]);
            var highlighted = WriteWave(directory, "Highlighted tone", 1200, [(8, true)]);
            var source = new ProbeSource();
            using var player = new MpvAudioPlayer(source);
            player.Configure(new AudioPreferences(CrossfadeSeconds: 1));
            player.SetVolume(1);
            await player.PlayAsync(first);
            await Task.Delay(650);
            var lowBands = player.Spectrum;
            Console.WriteLine($"Audio probe: elapsed={player.Elapsed.TotalSeconds:0.000}, peak band={Peak(lowBands)}, levels={string.Join(',', lowBands.Select(x => x.ToString("0.00")))}");
            Console.WriteLine($"Spectrum backend: {player.SpectrumBackend}");
            Console.WriteLine(player.SpectrumDiagnostics);
            var fft = player.SpectrumBackend == "WASAPI FFT";
            check(fft ? lowBands.Count == 48 && Math.Abs(Peak(lowBands) - FftSpectrumAnalyzer.BandForFrequency(440)) <= 1
                : lowBands.Count == 12 && Peak(lowBands) == 4, "440 Hz audio did not drive the matching spectrum band");
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
                check(fft, "Native process audio capture did not start on a supported system");
            player.TogglePause();
            await Task.Delay(150);
            var pausedAt = player.Elapsed;
            await Task.Delay(200);
            check(player.Spectrum.Count == 0 && Math.Abs((player.Elapsed - pausedAt).TotalSeconds) < 0.06,
                "Pause did not freeze playback and silence the spectrum");
            player.TogglePause();
            await player.PrepareAsync(second);
            await player.WarmSelectionAsync(highlighted);
            var wait = Stopwatch.StartNew();
            while (!player.ShouldTransition && player.IsPlaying && wait.Elapsed < TimeSpan.FromSeconds(8))
            {
                await Task.Delay(100);
                check(!(player.ShouldTransition && player.Elapsed.TotalSeconds < 4.8), "Internal silence was treated as the end of the track");
            }
            Console.WriteLine($"Audio probe: transition at source position {player.Elapsed.TotalSeconds:0.000}");
            check(player.ShouldTransition && player.Elapsed.TotalSeconds is >= 4.8 and < 6,
                "Trailing silence was not removed at the detected edge");
            var switchTimer = Stopwatch.StartNew();
            await player.PlayAsync(second);
            check(player.CurrentTrack == second && player.IsPlaying, "Prepared track did not start");
            check(source.RequestCount(second.Id) == 1, "Highlight preloading evicted the queued next track");
            if (fft)
            {
                while (player.Spectrum[FftSpectrumAnalyzer.BandForFrequency(2200)] < 0.15 && switchTimer.ElapsedMilliseconds < 500)
                    await Task.Delay(5);
                Console.WriteLine($"Prepared Next: new tone audible in {switchTimer.Elapsed.TotalMilliseconds:0} ms");
                check(switchTimer.ElapsedMilliseconds < 250, "Prepared manual playback still delayed the incoming song");
            }
            await Task.Delay(220);
            var manualIncoming = fft ? player.Spectrum[FftSpectrumAnalyzer.BandForFrequency(2200)] : 0;
            if (fft)
            {
                var overlap = player.Spectrum;
                Console.WriteLine($"Manual transition at 220ms: old={overlap[FftSpectrumAnalyzer.BandForFrequency(440)]:0.00}, new={overlap[FftSpectrumAnalyzer.BandForFrequency(2200)]:0.00}");
                check(overlap[FftSpectrumAnalyzer.BandForFrequency(440)] > 0.15
                    && overlap[FftSpectrumAnalyzer.BandForFrequency(2200)] > 0.15,
                    "Manual transition did not immediately introduce the new song while fading the old song");
            }
            await Task.Delay(1000);
            check(fft ? Math.Abs(Peak(player.Spectrum) - FftSpectrumAnalyzer.BandForFrequency(2200)) <= 1
                : Peak(player.Spectrum) is 7 or 8, "New track did not replace the frequency feed after crossfade");
            player.Seek(TimeSpan.FromSeconds(3));
            await Task.Delay(200);
            check(player.Elapsed.TotalSeconds > 3, "Seeking did not update the mpv playback position");
            player.Stop();
            check(!player.IsPlaying && player.Spectrum.Count == 0, "Stop left audio or spectrum active");
            player.Configure(new AudioPreferences(TrimSilence: false, CrossfadeSeconds: 0));
            await player.PlayAsync(first);
            await Task.Delay(250);
            check(player.Elapsed.TotalSeconds < 0.8 && player.Spectrum.All(value => value == 0),
                "Disabling trimming did not preserve the leading silence");
            check(!player.ShouldTransition, "Disabled crossfade triggered before the original track ending");
            player.Stop();
            if (fft)
            {
                var steady = WriteWave(directory, "Low tone", 440, [(8, true)]);
                player.Configure(new AudioPreferences(TrimSilence: false, CrossfadeSeconds: 3));
                await player.WarmSelectionAsync(steady);
                await player.PlayAsync(steady);
                check(source.RequestCount(steady.Id) == 1, "Manual playback did not reuse the highlighted track");
                await player.PrepareAsync(second);
                await Task.Delay(200);
                await player.TransitionAsync(second, automatic: true);
                await Task.Delay(220);
                var automaticIncoming = player.Spectrum[FftSpectrumAnalyzer.BandForFrequency(2200)];
                Console.WriteLine($"Automatic transition at 220ms: incoming={automaticIncoming:0.00} (manual={manualIncoming:0.00})");
                check(automaticIncoming > 0.01 && automaticIncoming < manualIncoming * 0.8,
                    "Automatic transitions did not retain a gradual incoming fade");
                player.Stop();
            }
            Console.WriteLine("PASS: real mpv frequency analysis, pause, edge silence, preloading, crossfade, seek, and cleanup.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static int Peak(IReadOnlyList<double> values) => values.Count == 0 ? -1
        : Enumerable.Range(0, values.Count).MaxBy(i => values[i]);

    private static Track WriteWave(string directory, string name, int frequency, (double Seconds, bool Tone)[] parts)
    {
        const int rate = 44100;
        var path = Path.Combine(directory, name + ".wav");
        var samples = (int)(parts.Sum(x => x.Seconds) * rate);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        var position = 0;
        foreach (var part in parts)
            for (var i = 0; i < (int)(part.Seconds * rate); i++, position++)
                writer.Write((short)(part.Tone ? Math.Sin(position * Math.Tau * frequency / rate) * 3000 : 0));
        return new Track("local:" + path, name, "Audio regression", TimeSpan.FromSeconds(samples / (double)rate));
    }

    private sealed class ProbeSource : IMusicSource
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _requests = new();
        public int RequestCount(string id) => _requests.GetValueOrDefault(id);
        public Task<string> GetPlayableSourceAsync(Track track, CancellationToken cancellationToken = default)
        {
            _requests.AddOrUpdate(track.Id, 1, (_, count) => count + 1);
            return Task.FromResult(track.Id["local:".Length..]);
        }
        public Task<IReadOnlyList<Track>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Track>>([]);
        public Task<PlaylistResult> GetPlaylistAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlaylistResult("Probe", []));
    }
}
