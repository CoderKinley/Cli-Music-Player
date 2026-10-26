namespace KMusicPlayer.UI;

public sealed record GuideEntry(string Section, string Keys, string Action, string Context);

public static class GuideCatalog
{
    public static IReadOnlyList<GuideEntry> Entries { get; } =
    [
        new("Navigation", "F1 / ?", "Open this keyboard guide", "Browsing screens and text prompts; F1 also works while typing."),
        new("Navigation", "Up / Down", "Move the selection", "Home panels, song lists, playlists, and themes."),
        new("Navigation", "Tab / Shift+Tab", "Focus the next / previous Home panel", "Home: Quick Links, Recently Played, Recent Favorites."),
        new("Navigation", "Left / Right", "Focus the previous / next Home panel", "Home; in the guide, switch categories."),
        new("Navigation", "PgUp / PgDn", "Move one page through a list", "Song lists, playlist library, guide, lyrics, and track details."),
        new("Navigation", "Home / End", "Jump to the first / last item", "Song lists, playlist library, guide, lyrics, and track details."),
        new("Navigation", "Enter", "Open the selected page or play the selected song", "Home, playlists, search results, favorites, and local music."),
        new("Navigation", "Q / Escape", "Go back; exit from Home", "In text fields, Escape cancels; all letters, including Q, are text."),
        new("Playback", "Space", "Pause, resume, or restore the previous session", "Home and song lists."),
        new("Playback", "N / P", "Play the next / previous song", "Home and song lists. In the playlist library, N creates a playlist."),
        new("Playback", "R", "Toggle shuffle", "Home and song lists; the setting is saved."),
        new("Playback", "S", "Stop playback", "Home and song lists."),
        new("Playback", ", / .", "Seek backward / forward 10 seconds", "Home and song lists."),
        new("Playback", "- / +", "Lower / raise volume by 5%", "Home and song lists; numeric keypad +/- also work."),
        new("Library", "F", "Add or remove the selected song as a favorite", "Home and song lists. In Favorites, removing it also removes its row."),
        new("Library", "A", "Add the selected song to a saved playlist", "Home and song lists; choose or create a playlist."),
        new("Library", "I", "Inspect the full song title, artist, and source", "Home and song lists. In the playlist library, I imports a playlist."),
        new("Library", "D", "Download the selected online song", "Home and song lists; use Local Music to browse downloaded audio."),
        new("Library", "Delete / Backspace", "Remove the selected song or playlist", "Inside a playlist: remove one song. Playlist library: delete the playlist. Favorites: remove the favorite."),
        new("Library", "I, paste URL, Enter", "Import a YouTube playlist", "Playlist library; public and unlisted playlist links are supported."),
        new("Library", "N, type name, Enter", "Create an empty playlist", "Playlist library. Then select a song elsewhere and press A to add it."),
        new("Search", "/", "Open music search", "Home and song lists. In this guide, / searches the shortcuts."),
        new("Search", "Type, Enter", "Search by song, artist, or album", "Search prompt. Long queries scroll within the field."),
        new("Search", "E", "Expand or collapse search results", "Search results initially show the first five songs."),
        new("Search", "Backspace", "Delete the previous character", "Text fields. In this guide, clear the shortcut filter."),
        new("Lyrics", "L", "Open lyrics for the selected song", "Home and song lists; L also closes the lyrics page."),
        new("Lyrics", "E", "Paste or edit lyrics manually", "Lyrics page. Enter .save on its own line to save, or .cancel to cancel."),
        new("Lyrics", ".save / .cancel", "Save / discard pasted lyrics", "Manual lyrics editor; these are typed commands, not keyboard shortcuts."),
        new("Settings", "Arrows, Enter", "Choose and apply a theme", "Themes: Blue, Spotify, and SoundCloud."),
        new("Settings", "Local Music", "Play offline audio or change the music folder", "Open Local Music from Home, then select the desired action."),
        new("Settings", "Backup & Restore", "Export or import all saved settings", "Home. Import creates a safety backup and closes Musik after restoring."),
        new("Settings", "musik --version", "Show the application version", "Run in PowerShell or Windows Terminal outside the app."),
        new("Settings", "musik update", "Install the latest GitHub release", "Run outside the app; requires an installer-based installation."),
        new("Settings", "winget install", "Install the WinGet package", "Run: winget install --id CoderKinley.Musik --exact --source winget")
    ];

    public static IReadOnlyList<GuideEntry> Find(string query, string? section = null)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Entries.Where(entry =>
            (section is null || entry.Section == section) && terms.All(term =>
                $"{entry.Section} {entry.Keys} {entry.Action} {entry.Context}".Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
