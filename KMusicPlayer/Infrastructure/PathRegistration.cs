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

        var installDirectory = Directory.GetParent(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
        if (string.IsNullOrWhiteSpace(installDirectory))
            return;

        using var environment = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
        if (environment is null)
            return;

        var current = environment.GetValue(
            "Path",
            string.Empty,
            RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
        var entries = current
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !string.Equals(
                entry.TrimEnd('\\'),
                installDirectory.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (add)
            entries.Add(installDirectory);

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
