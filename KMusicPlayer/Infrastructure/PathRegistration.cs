using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KMusicPlayer.Infrastructure;

public static class PathRegistration
{
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001a;
    private const int SmtoAbortIfHung = 0x0002;

    public static void AddInstallDirectory() => UpdatePath(add: true);
    public static void RemoveInstallDirectory() => UpdatePath(add: false);

    private static void UpdatePath(bool add)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var commandDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(commandDirectory))
            return;

        // Velopack places a launcher in the parent directory and the real console
        // executable in "current". Invoking the launcher detaches the application
        // and creates another terminal window. Adding "current" to PATH executes
        // musik.exe directly, keeping it attached to the caller's console.
        var launcherDirectory = Directory.GetParent(commandDirectory)?.FullName;
        using var environment = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
        if (environment is null)
            return;

        var current = environment.GetValue(
            "Path",
            string.Empty,
            RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
        var entries = current
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !PathsEqual(entry, commandDirectory))
            // Remove the v1.0.0 launcher entry while upgrading or uninstalling.
            .Where(entry => launcherDirectory is null || !PathsEqual(entry, launcherDirectory))
            .ToList();

        if (add)
            entries.Add(commandDirectory);

        environment.SetValue("Path", string.Join(';', entries), RegistryValueKind.ExpandString);
        SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            UIntPtr.Zero,
            "Environment",
            SmtoAbortIfHung,
            5000,
            out _);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd('\\', '/'),
            right.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        int windowHandle,
        int message,
        UIntPtr wParam,
        string lParam,
        int flags,
        int timeout,
        out UIntPtr result);
}
