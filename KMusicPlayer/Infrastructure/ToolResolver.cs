using System.Diagnostics;

namespace KMusicPlayer.Infrastructure;

public static class ToolResolver
{
    private static readonly Lazy<string> CachedYtDlpPath = new(ResolveYtDlpInternal);
    private static readonly Lazy<string> CachedMpvPath = new(ResolveMpvInternal);
    private static readonly Lazy<bool> CachedHasNode = new(CheckNodeInternal);

    public static string ResolveMpvExecutable() => CachedMpvPath.Value;

    public static string ResolveYtDlpExecutable() => CachedYtDlpPath.Value;

    public static bool HasNodeJs() => CachedHasNode.Value;

    public static string GetToolsDirectory()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KMusicPlayer",
            "tools");
        Directory.CreateDirectory(dataDirectory);
        return dataDirectory;
    }

    public static async Task<string> EnsureYtDlpAsync(CancellationToken cancellationToken = default)
    {
        var existing = ResolveYtDlpExecutable();
        if (File.Exists(existing))
            return existing;

        var targetDirectory = GetToolsDirectory();
        var targetFile = Path.Combine(targetDirectory, "yt-dlp.exe");
        if (File.Exists(targetFile))
            return targetFile;

        var tempFile = targetFile + ".tmp";
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "KMusicPlayer/1.3.0");
            using var response = await client.GetAsync(
                "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            if (File.Exists(targetFile))
                File.Delete(targetFile);

            File.Move(tempFile, targetFile);
            return targetFile;
        }
        catch
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
            return existing;
        }
    }

    private static string ResolveMpvInternal()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "mpv.exe");
        if (File.Exists(bundled))
            return bundled;

        var localTools = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KMusicPlayer",
            "tools",
            "mpv.exe");
        if (File.Exists(localTools))
            return localTools;

        return "mpv";
    }

    private static string ResolveYtDlpInternal()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
        if (File.Exists(bundled))
            return bundled;

        var localTools = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KMusicPlayer",
            "tools",
            "yt-dlp.exe");
        if (File.Exists(localTools))
            return localTools;

        var chocoPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "chocolatey",
            "bin",
            "yt-dlp.exe");
        if (File.Exists(chocoPath))
            return chocoPath;

        var pathExecutable = FindInPath("yt-dlp.exe") ?? FindInPath("yt-dlp");
        if (!string.IsNullOrWhiteSpace(pathExecutable))
            return pathExecutable;

        return "yt-dlp";
    }

    private static bool CheckNodeInternal()
    {
        try
        {
            var nodePath = FindInPath("node.exe") ?? FindInPath("node");
            if (!string.IsNullOrWhiteSpace(nodePath))
                return true;

            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(1000);
                return process.ExitCode == 0;
            }
        }
        catch
        {
        }
        return false;
    }

    private static string? FindInPath(string filename)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path.Trim(), filename);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
