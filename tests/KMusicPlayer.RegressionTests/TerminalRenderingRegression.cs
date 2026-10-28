using KMusicPlayer.UI;
using KMusicPlayer.UI.Themes;

internal static class TerminalRenderingRegression
{
    public static void Run(Action<bool, string> check)
    {
        // These checks exercise the Windows console viewport as well as the output buffer.
        if (Console.IsOutputRedirected) return;
        var output = Console.Out;
        var theme = ThemeManager.Current;
        using var captured = new CountingWriter();
        try
        {
            Console.SetOut(captured);
            ThemeManager.Apply(TerminalTheme.Blue);
            using (TerminalCanvas.BeginFrame())
            {
                TerminalCanvas.WriteAt(0, 0, "A", ConsoleColor.Black, ConsoleColor.Blue);
                using (TerminalCanvas.BeginFrame())
                    TerminalCanvas.WriteAt(0, 0, "B", ConsoleColor.Cyan);
                check(captured.Writes == 0, "A nested panel flushed before the whole frame was ready");
            }
            check(captured.Writes == 1, "A terminal frame used more than one console write");
            check(captured.ToString() == "\u001b[1;1H\u001b[30;104mA\u001b[0m\u001b[1;1H\u001b[96;49mB\u001b[0m",
                "Batched terminal drawing changed cursor positions, colors, or write order");

            captured.GetStringBuilder().Clear();
            int[] colors = [30, 34, 32, 36, 31, 35, 33, 37, 90, 94, 92, 96, 91, 95, 93, 97];
            using (TerminalCanvas.BeginFrame())
                foreach (var color in Enum.GetValues<ConsoleColor>())
                    TerminalCanvas.WriteAt(0, 0, "X", color, color);
            foreach (var color in colors)
                check(captured.ToString().Contains($"\u001b[{color};{(color == 30 ? 49 : color + 10)}mX\u001b[0m"),
                    "A console palette color changed in batched output");

            captured.GetStringBuilder().Clear();
            using (TerminalCanvas.BeginFrame())
            {
                TerminalCanvas.WriteMenuItem(0, 0, 1, "Menu", false);
                var player = new PlaybackPanel();
                var frame = new PlaybackPanelFrame(0, 10, TerminalCanvas.Version, ThemeManager.Current,
                    "Playing", "Track", "0:01", true, 3);
                player.Draw(frame);
                player.Draw(frame with { Filled = 1 });
            }
            check(!captured.ToString().Contains(";40m") && !captured.ToString().Contains("\u001b[40m")
                && captured.ToString().Contains(";49m") && captured.ToString().Contains(";104m"),
                "Menus or playback painted black, or lost their colored progress fill");

            captured.GetStringBuilder().Clear();
            using (TerminalCanvas.BeginFrame())
            {
                TerminalCanvas.WriteAt(0, 0, "\u001b", ConsoleColor.Gray);
                TerminalCanvas.WriteAt(TerminalCanvas.Width, 0, "hidden", ConsoleColor.Gray);
                TerminalCanvas.WriteAt(0, TerminalCanvas.Height, "hidden", ConsoleColor.Gray);
            }
            check(captured.ToString() == "\u001b[1;1H\u001b[37;49m \u001b[0m",
                "Batched output bypassed metadata sanitization or viewport clipping");

            var writes = captured.Writes;
            try
            {
                using var frame = TerminalCanvas.BeginFrame();
                TerminalCanvas.WriteAt(0, 0, "C", ConsoleColor.Gray);
                throw new InvalidOperationException("Drawing probe");
            }
            catch (InvalidOperationException) { }
            TerminalCanvas.WriteAt(0, 0, "D", ConsoleColor.Gray);
            check(captured.Writes == writes + 2, "A failed drawing callback left subsequent output buffered");
        }
        finally
        {
            Console.SetOut(output);
            ThemeManager.Apply(theme);
        }
    }

    private sealed class CountingWriter : StringWriter
    {
        public int Writes { get; private set; }
        public override void Write(string? value) { Writes++; base.Write(value); }
    }
}
