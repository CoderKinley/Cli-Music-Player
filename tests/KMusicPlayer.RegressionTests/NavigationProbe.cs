using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using KMusicPlayer.Domain;
using KMusicPlayer.UI;

internal static class NavigationProbe
{
    public static void Run(bool longRun)
    {
        var draw = typeof(TerminalApplication).GetMethod("DrawResultRows", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tracks = Enumerable.Range(0, 1000)
            .Select(i => new Track($"probe-{i}", $"Track {i}: a representative song title", "Artist name", TimeSpan.FromMinutes(3))).ToArray();
        var favorites = new HashSet<string>();
        var measurements = new List<object>();
        var spectrum = new SpectrumPanel();
        var levels = new double[48];
        Console.CursorVisible = false;
        foreach (var animated in new[] { false, true })
        {
            TerminalCanvas.Clear();
            var elapsed = new double[longRun ? 1000 : 100];
            for (var i = 0; i < elapsed.Length; i++)
            {
                for (var band = 0; band < levels.Length; band++) levels[band] = 0.5 + Math.Sin(i * 0.1 + band * 0.4) * 0.45;
                var started = Stopwatch.GetTimestamp();
                draw.Invoke(null, [tracks, i, favorites, i == 0 ? null : (object)(i - 1)]);
                if (animated) spectrum.Draw(2, 3, TerminalCanvas.Width - 4, Math.Max(1, TerminalCanvas.Height - 14), SpectrumStyle.CatHalo, levels);
                elapsed[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            measurements.Add(new { Mode = animated ? "list-and-spectrum" : "list", MeanMs = elapsed.Average(), P95Ms = elapsed.Order().ElementAt(elapsed.Length * 95 / 100), FirstTenMs = elapsed.Take(10).Average(), LastTenMs = elapsed.TakeLast(10).Average() });
        }
        foreach (var style in new[] { SpectrumStyle.CatHalo, SpectrumStyle.CatOutline })
        {
            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < 100; i++) SpectrumPanel.Render(240, 60, style, levels);
            measurements.Add(new { Mode = $"{style}-render-240x60", MeanMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds / 100 });
        }
        TerminalCanvas.Clear();
        Console.CursorVisible = true;
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/navigation-probe.json", JsonSerializer.Serialize(new { Width = Console.WindowWidth, Height = Console.WindowHeight, Measurements = measurements }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Navigation timings saved to artifacts/navigation-probe.json");
    }
}
