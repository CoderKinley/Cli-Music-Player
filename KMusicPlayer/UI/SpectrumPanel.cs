using System.Text;
using System.Diagnostics;

namespace KMusicPlayer.UI;

public enum SpectrumStyle { CatOutline, CatHalo, AuroraBars, AuroraWave, Orbit, SilkWave, Off }

/// <summary>Braille dots provide a 2 x 4 pixel surface per terminal cell.</summary>
public sealed class SpectrumPanel
{
    private readonly double[] _levels = new double[48];
    private long _lastFrame;
    private string[] _previous = [];
    private (int X, int Y, int W, int H, long Version) _bounds;

    public static string Label(SpectrumStyle style) => style switch
    {
        SpectrumStyle.CatOutline => "Cat Outline",
        SpectrumStyle.CatHalo => "Cat Halo",
        SpectrumStyle.AuroraBars => "Aurora Bars",
        SpectrumStyle.AuroraWave => "Aurora Wave",
        SpectrumStyle.SilkWave => "Silk Wave",
        _ => style.ToString()
    };

    public void Draw(int x, int y, int width, int height, SpectrumStyle style, IReadOnlyList<double> samples)
    {
        if (width <= 0 || height <= 0) return;
        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastFrame == 0 ? 1d / 60 : Math.Clamp(Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds, 0, 0.1);
        _lastFrame = now;
        for (var i = 0; i < _levels.Length; i++)
        {
            var position = samples.Count > 1 ? i * (samples.Count - 1d) / (_levels.Length - 1) : 0;
            var low = (int)position;
            var target = samples.Count == 0 ? 0 : samples[low] * (1 - position + low)
                + samples[Math.Min(low + 1, samples.Count - 1)] * (position - low);
            target = double.IsFinite(target) ? Math.Clamp(target, 0, 1) : 0;
            // Time-based attack/release has the same feel on fast and slow terminals.
            _levels[i] += (target - _levels[i]) * (1 - Math.Exp(-elapsed / (target > _levels[i] ? 0.008 : 0.085)));
            if (_levels[i] < 0.001) _levels[i] = 0;
        }
        var frame = Render(width, height, style, _levels);
        var bounds = (x, y, width, height, TerminalCanvas.Version);
        if (_bounds != bounds) _previous = [];
        _bounds = bounds;
        var output = new StringBuilder();
        for (var row = 0; row < frame.Length; row++)
        {
            var old = row < _previous.Length ? _previous[row] : null;
            if (old == frame[row]) continue;
            for (var column = 0; column < width;)
            {
                if (old is not null && old[column] == frame[row][column]) { column++; continue; }
                var start = column;
                var stripe = column * 12 / width;
                do { column++; }
                while (column < width && column * 12 / width == stripe && (old is null || old[column] != frame[row][column]));
                var (r, g, b) = Color(stripe / 11d);
                output.Append($"\u001b[{y + row + 1};{x + start + 1}H\u001b[38;2;{r};{g};{b}m\u001b[40m");
                output.Append(frame[row].AsSpan(start, column - start));
            }
        }
        if (output.Length > 0) Console.Write(output.Append("\u001b[0m").ToString());
        _previous = frame;
    }

    private static (int, int, int) Color(double position)
    {
        // Sea glass -> silver -> muted iris, independent of the surrounding UI theme.
        var a = position < 0.5 ? (70, 193, 184) : (205, 219, 230);
        var b = position < 0.5 ? (205, 219, 230) : (154, 138, 204);
        var t = position < 0.5 ? position * 2 : (position - 0.5) * 2;
        return ((int)(a.Item1 + (b.Item1 - a.Item1) * t), (int)(a.Item2 + (b.Item2 - a.Item2) * t),
            (int)(a.Item3 + (b.Item3 - a.Item3) * t));
    }

    public static string[] Render(int width, int height, SpectrumStyle style, IReadOnlyList<double> levels)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        var dots = new byte[width * height];
        if (width == 0 || height == 0) return Enumerable.Repeat("", height).ToArray();
        var pw = width * 2;
        var ph = height * 4;
        double Level(double t)
        {
            if (levels.Count == 0) return 0;
            var index = Math.Clamp(t, 0, 1) * (levels.Count - 1);
            var low = (int)index;
            var value = levels[low] * (1 - index + low) + levels[Math.Min(low + 1, levels.Count - 1)] * (index - low);
            return double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        }
        void Dot(double x, double y)
        {
            var ix = (int)Math.Round(x);
            var iy = (int)Math.Round(y);
            if (ix < 0 || ix >= pw || iy < 0 || iy >= ph) return;
            ReadOnlySpan<byte> bits = [1, 8, 2, 16, 4, 32, 64, 128];
            dots[iy / 4 * width + ix / 2] |= bits[iy % 4 * 2 + ix % 2];
        }
        void Line(double x1, double y1, double x2, double y2)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) * 1.5));
            for (var i = 0; i <= steps; i++) Dot(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
        }

        var cx = (pw - 1) / 2d;
        var cy = (ph - 1) / 2d;
        if (style == SpectrumStyle.CatOutline)
        {
            (double X, double Y)[][] curves =
            [
                [(0.12, 0.86), (0.12, 0.12), (0.27, 0.02), (0.30, 0.42)],
                [(0.30, 0.42), (0.32, 0.37), (0.36, 0.37), (0.39, 0.42)],
                [(0.39, 0.42), (0.42, 0.02), (0.53, 0.05), (0.56, 0.51)],
                [(0.56, 0.51), (0.77, 0.48), (0.84, 0.57), (0.90, 0.85)],
                [(0.90, 0.85), (0.93, 0.86), (0.96, 0.83), (0.99, 0.84)]
            ];
            var sw = Math.Min(pw - 6d, (ph - 4d) * 1.9);
            var sh = Math.Min(ph - 4d, sw / 1.9);
            var left = cx - sw / 2;
            var top = cy - sh / 2;
            foreach (var c in curves)
                for (var i = 0; i <= 160; i++)
                {
                    var t = i / 160d;
                    var u = 1 - t;
                    var x = u * u * u * c[0].X + 3 * u * u * t * c[1].X + 3 * u * t * t * c[2].X + t * t * t * c[3].X;
                    var y = u * u * u * c[0].Y + 3 * u * u * t * c[1].Y + 3 * u * t * t * c[2].Y + t * t * t * c[3].Y;
                    var energy = Level(x);
                    var dx = 3 * u * u * (c[1].X - c[0].X) + 6 * u * t * (c[2].X - c[1].X) + 3 * t * t * (c[3].X - c[2].X);
                    var dy = 3 * u * u * (c[1].Y - c[0].Y) + 6 * u * t * (c[2].Y - c[1].Y) + 3 * t * t * (c[3].Y - c[2].Y);
                    var length = Math.Max(0.001, Math.Sqrt(dx * dx * sw * sw + dy * dy * sh * sh));
                    var nx = dy * sh / length;
                    var ny = -dx * sw / length;
                    var motion = energy * sh * 0.07;
                    Dot(left + x * sw + nx * motion, top + y * sh + ny * motion);
                    if (i % 4 == 0)
                        Line(left + x * sw, top + y * sh, left + x * sw + nx * energy * sh * 0.32,
                            top + y * sh + ny * energy * sh * 0.32);
                }
        }
        else if (style is SpectrumStyle.CatHalo or SpectrumStyle.Orbit)
        {
            var bass = Enumerable.Range(0, 8).Average(i => Level(i / 48d));
            var radius = Math.Max(1, Math.Min(pw * 0.29, ph * 0.25)) * (1 + bass * 0.12);
            for (var i = 0; i < 280; i++)
            {
                var angle = i * Math.Tau / 280 - Math.PI / 2;
                var energy = Level(Math.Abs(i / 140d - 1));
                var r = radius * (1 + energy * 0.14);
                Dot(cx + Math.Cos(angle) * r, cy + Math.Sin(angle) * r);
                if (i % 4 == 0)
                    Line(cx + Math.Cos(angle) * (r + 1), cy + Math.Sin(angle) * (r + 1),
                        cx + Math.Cos(angle) * (r + 1 + radius * energy * 0.65), cy + Math.Sin(angle) * (r + 1 + radius * energy * 0.65));
                if (style == SpectrumStyle.Orbit)
                    Dot(cx + Math.Cos(angle) * radius * 0.7, cy + Math.Sin(angle) * radius * 0.7);
            }
            if (style == SpectrumStyle.CatHalo)
                foreach (var side in new[] { -1, 1 })
                {
                    var x1 = cx + side * radius * 0.78;
                    var x2 = cx + side * radius * 0.80;
                    var x3 = cx + side * radius * 0.27;
                    var y1 = cy - radius * 0.61;
                    var y2 = cy - radius * (1.45 + bass * 0.28);
                    var y3 = cy - radius * 0.98;
                    Line(x1, y1, x2, y2); Line(x2, y2, x3, y3);
                    Line(x1 + side * 2, y1 - 1, x2 + side, y2 + 3);
                }
        }
        else if (style == SpectrumStyle.AuroraBars)
        {
            var count = Math.Max(4, Math.Min(48, pw / 3));
            for (var i = 0; i < count; i++)
            {
                var energy = Level(i / (double)(count - 1));
                var x = 3 + (pw - 7d) * i / (count - 1);
                var length = 1 + energy * Math.Max(1, ph * 0.74);
                Line(x, ph * 0.87, x, ph * 0.87 - length);
                Line(x + 1, ph * 0.87, x + 1, ph * 0.87 - length);
                if (energy > 0.05) Dot(x, ph * 0.87 - length - 2);
            }
        }
        else if (style == SpectrumStyle.AuroraWave)
        {
            void MirrorDot(double x, double upperY)
            {
                var y = (int)Math.Round(upperY);
                Dot(x, y);
                Dot(x, ph - 1 - y);
            }
            var previousTop = cy;
            for (var x = 2; x < pw - 2; x++)
            {
                var t = (x - 2d) / Math.Max(1, pw - 5);
                // Window the ends into the centerline; every crest comes from a measured band.
                var taper = Math.Pow(Math.Sin(t * Math.PI), 0.45);
                var amplitude = 0.5 + Level(t) * taper * Math.Max(0, ph * 0.43 - 1);
                var top = cy - amplitude;
                if (x > 2)
                {
                    var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(top - previousTop) * 2));
                    for (var step = 0; step <= steps; step++)
                        MirrorDot(x - 1 + step / (double)steps, previousTop + (top - previousTop) * step / steps);
                }
                if (x % 3 == 0)
                    for (var y = (int)Math.Round(top); y <= cy; y++) MirrorDot(x, y);
                MirrorDot(x, cy - amplitude * 0.58);
                previousTop = top;
            }
        }
        else if (style == SpectrumStyle.SilkWave)
        {
            for (var layer = 0; layer < 3; layer++)
                for (var x = 2; x < pw - 2; x++)
                {
                    var t = (x - 2d) / Math.Max(1, pw - 5);
                    var envelope = Level(t) * Math.Sin(t * Math.PI);
                    Dot(x, cy + Math.Sin(t * Math.Tau * (2 + layer) + layer * 0.8) * envelope * ph * (0.32 - layer * 0.07));
                }
        }
        return Enumerable.Range(0, height).Select(row => new string(Enumerable.Range(0, width)
            .Select(col => dots[row * width + col] == 0 ? ' ' : (char)(0x2800 + dots[row * width + col])).ToArray())).ToArray();
    }
}
