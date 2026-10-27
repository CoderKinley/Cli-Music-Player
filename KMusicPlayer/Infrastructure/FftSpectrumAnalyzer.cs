using System.Diagnostics;
using System.Numerics;

namespace KMusicPlayer.Infrastructure;

/// <summary>Overlapping Hann FFTs: a short window for transients and a longer bass window.</summary>
public sealed class FftSpectrumAnalyzer
{
    public const int SampleRate = 48000;
    public const int BandCount = 48;
    public const int HopSize = 512;
    private const int BassSize = 4096;
    private const int TrebleSize = 2048;
    private readonly float[][] _ring = [new float[BassSize], new float[BassSize]];
    private readonly Complex[] _fft = new Complex[BassSize];
    private readonly double[] _bassPower = new double[BassSize / 2];
    private readonly double[] _treblePower = new double[TrebleSize / 2];
    private readonly double[] _bassWindow = Window(BassSize);
    private readonly double[] _trebleWindow = Window(TrebleSize);
    private int _position, _count, _hop;
    private double _referenceDb = -36;
    private bool _hasReference;
    private double[] _levels = new double[BandCount];
    private long _publishedAt;
    public long FramesPublished { get; private set; }
    public double[] Levels => Stopwatch.GetElapsedTime(Volatile.Read(ref _publishedAt)).TotalMilliseconds < 120
        ? Volatile.Read(ref _levels) : new double[BandCount];

    public static double Frequency(int band) => 45 * Math.Pow(18000d / 45, (band + 0.5) / BandCount);
    public static int BandForFrequency(double frequency) => Math.Clamp((int)(Math.Log(frequency / 45) / Math.Log(400) * BandCount), 0, BandCount - 1);

    // Called by one capture thread. Stereo power is combined after FFT to retain anti-phase audio.
    public void Add(ReadOnlySpan<short> interleavedStereo)
    {
        for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
            AddSample(interleavedStereo[i] / 32768f, interleavedStereo[i + 1] / 32768f);
    }

    public void Add(ReadOnlySpan<float> interleavedStereo)
    {
        for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
            AddSample(interleavedStereo[i], interleavedStereo[i + 1]);
    }

    private void AddSample(float left, float right)
    {
        _ring[0][_position] = float.IsFinite(left) ? left : 0;
        _ring[1][_position] = float.IsFinite(right) ? right : 0;
        _position = (_position + 1) % BassSize;
        _count = Math.Min(_count + 1, BassSize);
        if (++_hop < HopSize) return;
        _hop = 0;
        Publish();
    }

    public void Reset()
    {
        foreach (var channel in _ring) Array.Clear(channel);
        _count = _position = _hop = 0;
        Volatile.Write(ref _levels, new double[BandCount]);
    }

    private void Publish()
    {
        TransformPower(TrebleSize, _trebleWindow, _treblePower);
        TransformPower(BassSize, _bassWindow, _bassPower);
        var db = new double[BandCount];
        var peak = -240d;
        for (var band = 0; band < BandCount; band++)
        {
            var low = 45 * Math.Pow(400, band / (double)BandCount);
            var high = 45 * Math.Pow(400, (band + 1d) / BandCount);
            var bass = Frequency(band) < 220;
            var power = bass ? _bassPower : _treblePower;
            var size = bass ? BassSize : TrebleSize;
            var first = low * size / SampleRate;
            var last = high * size / SampleRate;
            var energy = 0d;
            // Fractional bin overlap avoids repeating the same bass bin across several bands.
            for (var bin = Math.Max(1, (int)Math.Floor(first)); bin <= Math.Min(power.Length - 1, Math.Ceiling(last)); bin++)
            {
                var weight = Math.Max(0, Math.Min(last, bin + 0.5) - Math.Max(first, bin - 0.5));
                energy += power[bin] * weight;
            }
            db[band] = 10 * Math.Log10(Math.Max(1e-24, energy));
            peak = Math.Max(peak, db[band]);
        }
        if (!_hasReference && peak > -190 && _count >= TrebleSize)
        {
            _referenceDb = Math.Max(-170, peak + 10);
            _hasReference = true;
        }
        var target = Math.Max(-170, peak + 10);
        var dt = HopSize / (double)SampleRate;
        if (peak > -190)
            _referenceDb += (target - _referenceDb) * (1 - Math.Exp(-dt / (target > _referenceDb ? 0.08 : 1.8)));
        var floor = Math.Max(-190, _referenceDb - 42);
        var levels = new double[BandCount];
        if (peak > -190 && _count >= TrebleSize)
            for (var i = 0; i < BandCount; i++)
                levels[i] = Math.Pow(Math.Clamp((db[i] - floor) / Math.Max(24, _referenceDb - floor), 0, 1), 1.8);
        Volatile.Write(ref _levels, levels);
        Volatile.Write(ref _publishedAt, Stopwatch.GetTimestamp());
        FramesPublished++;
    }

    private void TransformPower(int size, double[] window, double[] power)
    {
        Array.Clear(power);
        for (var channel = 0; channel < 2; channel++)
        {
            for (var i = 0; i < size; i++)
                _fft[i] = new Complex(_ring[channel][(_position - size + i + BassSize) % BassSize] * window[i], 0);
            Transform(_fft.AsSpan(0, size));
            var scale = 8d / (size * (double)size); // Hann coherent gain and stereo power average.
            for (var i = 1; i < power.Length; i++)
                power[i] += (_fft[i].Real * _fft[i].Real + _fft[i].Imaginary * _fft[i].Imaginary) * scale;
        }
    }

    private static double[] Window(int size) => Enumerable.Range(0, size)
        .Select(i => 0.5 - 0.5 * Math.Cos(Math.Tau * i / (size - 1))).ToArray();

    private static void Transform(Span<Complex> data)
    {
        for (int i = 1, j = 0; i < data.Length; i++)
        {
            var bit = data.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var length = 2; length <= data.Length; length <<= 1)
        {
            var rotation = Complex.FromPolarCoordinates(1, -Math.Tau / length);
            for (var start = 0; start < data.Length; start += length)
            {
                var phase = Complex.One;
                for (var i = 0; i < length / 2; i++)
                {
                    var even = data[start + i];
                    var odd = data[start + i + length / 2] * phase;
                    data[start + i] = even + odd;
                    data[start + i + length / 2] = even - odd;
                    phase *= rotation;
                }
            }
        }
    }
}
