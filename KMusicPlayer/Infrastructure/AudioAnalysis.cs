using System.Globalization;

namespace KMusicPlayer.Infrastructure;

/// <summary>Measure frequency energy on a separate branch, preserving the audible signal.</summary>
public static class AudioAnalysis
{
    public static readonly int[] Frequencies = [55, 90, 150, 250, 420, 700, 1170, 1950, 3260, 5450, 9100, 15200];

    public static string CreateFilter(bool trimStart)
    {
        var head = trimStart
            ? "silenceremove=start_periods=1:start_duration=0.02:start_threshold=-55dB:start_silence=0.025:timestamp=copy,"
            : "";
        var inputs = string.Concat(Frequencies.Select((_, i) => $"[b{i}]"));
        var outputs = string.Concat(Frequencies.Select((_, i) => $"[f{i}]"));
        var branches = string.Join(';', Frequencies.Select((frequency, i) =>
            $"[b{i}]bandpass=f={frequency}:t=o:w=1,aformat=channel_layouts=mono[f{i}]"));
        return head + $"asplit=2[out][analysis];[analysis]aformat=channel_layouts=mono,aresample=44100,asplit={Frequencies.Length}{inputs};" +
            branches + $";{outputs}amerge=inputs={Frequencies.Length},asetnsamples=n=2048," +
            "astats=metadata=1:reset=1:measure_perchannel=RMS_level:measure_overall=none,ametadata=print:file=-:direct=1,anullsink";
    }

    public static bool TryParseBand(string line, out int band, out double level)
    {
        band = -1;
        level = 0;
        const string prefix = "lavfi.astats.";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var separator = line.IndexOf(".RMS_level=", prefix.Length, StringComparison.Ordinal);
        if (separator < 0 || !int.TryParse(line.AsSpan(prefix.Length, separator - prefix.Length), out var channel)
            || channel < 1 || channel > Frequencies.Length) return false;
        var value = line.AsSpan(separator + ".RMS_level=".Length);
        band = channel - 1;
        if (value.SequenceEqual("-inf")) return true;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) || !double.IsFinite(db)) return false;
        level = Math.Clamp((db + 65) / 55, 0, 1);
        return true;
    }

    public static (double Incoming, double Outgoing) FadeGains(double progress) =>
        (Math.Sin(Math.Clamp(progress, 0, 1) * Math.PI / 2), Math.Cos(Math.Clamp(progress, 0, 1) * Math.PI / 2));
}
