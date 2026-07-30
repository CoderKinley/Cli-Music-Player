using Velopack;
using Velopack.Sources;

namespace KMusicPlayer.Infrastructure;

public static class ApplicationUpdater
{
    private const string RepositoryUrl =
        "https://github.com/CoderKiLe/KMusiKPlayer-Cli";

    public static async Task<int> RunAsync()
    {
        try
        {
            var manager = new UpdateManager(
                new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

            if (!manager.IsInstalled)
            {
                Console.WriteLine("Updates are available only for the installed version of Musik.");
                Console.WriteLine("Download Musik-Setup.exe from the GitHub Releases page first.");
                return 2;
            }

            Console.WriteLine($"Current version: {manager.CurrentVersion}");
            Console.WriteLine("Checking for updates...");
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Console.WriteLine("Musik is up to date.");
                return 0;
            }

            Console.WriteLine($"Downloading version {update.TargetFullRelease.Version}...");
            await manager.DownloadUpdatesAsync(
                update,
                progress => Console.Write($"\rDownload progress: {progress,3}%"));
            Console.WriteLine();
            Console.WriteLine("Applying update and restarting Musik...");
            manager.ApplyUpdatesAndRestart(update);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Update failed: {exception.Message}");
            return 1;
        }
    }
}
