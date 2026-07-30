using KMusicPlayer.Application;
using KMusicPlayer.Infrastructure;
using KMusicPlayer.UI;
using Velopack;

namespace KMusicPlayer;

internal static class Program
{
    public static void Main(string[] args)
    {
        var velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            velopack
                .OnAfterInstallFastCallback(_ => PathRegistration.AddInstallDirectory())
                .OnBeforeUninstallFastCallback(_ => PathRegistration.RemoveInstallDirectory());
        }
        velopack.Run();
        RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(string[] args)
    {
        if (args.Length > 0 &&
            args[0].Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = await ApplicationUpdater.RunAsync();
            return;
        }

        if (args.Length > 0 &&
            args[0].Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Musik 1.0.0");
            return;
        }

        Console.Title = "Cli Musik";
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        TerminalWindow.Configure(width: 100, height: 30);

        var source = new YouTubeMusicSource();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KMusicPlayer");

        var favorites = new JsonFavoriteRepository(
            Path.Combine(dataDirectory, "favorites.json"));
        var history = new JsonHistoryRepository(
            Path.Combine(dataDirectory, "history.json"));
        var settings = new JsonSettingsRepository(
            Path.Combine(dataDirectory, "settings.json"));
        var session = new JsonPlaybackSessionRepository(
            Path.Combine(dataDirectory, "session.json"));

        using var player = new MpvAudioPlayer(source);
        var app = new MusicApplication(
            source,
            favorites,
            player,
            history,
            settings,
            session);
        await app.InitializeAsync();

        var terminal = new TerminalApplication(app);
        await terminal.RunAsync();
    }
}
