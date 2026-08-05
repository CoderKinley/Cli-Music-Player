using KMusicPlayer.Application;
using KMusicPlayer.Domain;
using KMusicPlayer.UI.Screens;
using KMusicPlayer.UI.Themes;
using static KMusicPlayer.UI.TerminalCanvas;
using static KMusicPlayer.UI.TerminalInput;

namespace KMusicPlayer.UI;

public sealed class TerminalApplication
{
    private readonly MusicApplication _app;
    private readonly PlaybackQueueController _playbackQueue;
    private readonly SearchScreen _searchScreen;
    private readonly PlaylistScreen _playlistScreen;
    private readonly ThemesScreen _themesScreen;
    private readonly LyricsScreen _lyricsScreen;
    private readonly ITrackDownloadService _downloads;
    private readonly ILocalMusicLibrary _localLibrary;
    private readonly ISettingsTransferService _settingsTransfer;
    private bool _importedSettings;
    private string _status = "Ready";

    public TerminalApplication(
        MusicApplication app,
        ILyricsService lyrics,
        ITrackDownloadService downloads,
        ILocalMusicLibrary localLibrary,
        ISettingsTransferService settingsTransfer,
        IPlaylistRepository playlists)
    {
        _app = app;
        _playbackQueue = new PlaybackQueueController(app);
        _playbackQueue.SetShuffle(app.ShuffleEnabled);
        if (Enum.TryParse<TerminalTheme>(app.ThemeName, true, out var savedTheme))
            ThemeManager.Apply(savedTheme);
        _searchScreen = new SearchScreen(
            app,
            (title, tracks) => BrowseTracksAsync(title, tracks, false, collapsible: true),
            status => _status = status);
        _playlistScreen = new PlaylistScreen(
            app,
            playlists,
            (title, tracks) => BrowseTracksAsync(title, tracks, false, loopQueue: true),
            status => _status = status);
        _themesScreen = new ThemesScreen(app);
        _lyricsScreen = new LyricsScreen(lyrics);
        _downloads = downloads;
        _localLibrary = localLibrary;
        _settingsTransfer = settingsTransfer;
        if (app.PreviousSession is not null)
            _status = "Previous session ready - press Space to resume";
    }

    public async Task RunAsync()
    {
        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                var selected = await ShowDashboardAsync();
                if (selected is -1 or 7)
                    return;

                if (selected == 0)
                    await _searchScreen.ShowAsync();
                else if (selected == 1)
                    await _playlistScreen.ShowAsync();
                else if (selected == 2)
                    await ShowFavoritesAsync();
                else if (selected == 3)
                    await BrowseTracksAsync("RECENTLY PLAYED", _app.RecentlyPlayed, false, loopQueue: true);
                else if (selected == 4)
                    await _themesScreen.ShowAsync();
                else if (selected == 5)
                    await ShowLocalMusicAsync();
                else if (selected == 6)
                    await ShowSettingsTransferAsync();

                if (_importedSettings)
                    return;
            }
        }
        finally
        {
            try
            {
                if (!_importedSettings)
                    await _app.SavePlaybackSessionAsync();
            }
            catch
            {
                // Shutdown must continue even if the checkpoint cannot be written.
            }
            _app.Stop();
            Console.ResetColor();
            Console.CursorVisible = true;
            Clear();
        }
    }

    private async Task<int> ShowDashboardAsync()
    {
        var panel = HomePanel.QuickLinks;
        var linkSelection = 0;
        var recentSelection = 0;
        var favoriteSelection = 0;
        var links = new[]
        {
            "Search", "Playlists", "Favorites", "Recently Played", "Themes",
            "Local Music", "Backup & Restore", "Quit"
        };
        var renderedWidth = -1;
        var renderedHeight = -1;
        var contentDirty = false;
        HomePanel? selectionDirtyPanel = null;
        var favorites = await SafeFavoritesAsync();

        while (true)
        {
            recentSelection = ClampSelection(recentSelection, _app.RecentlyPlayed.Count);
            favoriteSelection = ClampSelection(favoriteSelection, favorites.Count);
            if (selectionDirtyPanel is not null && renderedWidth >= 0)
            {
                DrawDashboardPanelContent(
                    selectionDirtyPanel.Value,
                    links,
                    linkSelection,
                    recentSelection,
                    favoriteSelection,
                    favorites);
                selectionDirtyPanel = null;
            }
            else if (contentDirty && renderedWidth >= 0)
            {
                DrawDashboardContent(
                    links, panel, linkSelection, recentSelection, favoriteSelection, favorites);
                contentDirty = false;
            }
            var currentPanel = panel;
            var currentLink = linkSelection;
            var currentRecent = recentSelection;
            var currentFavorite = favoriteSelection;
            var key = ReadResponsive(
                () => DrawDashboard(
                    links, currentPanel, currentLink, currentRecent, currentFavorite, favorites),
                ref renderedWidth,
                ref renderedHeight,
                HasFinishedTrack,
                () => DrawNowPlaying(Math.Max(17, Height() - 8), Width()));

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (panel == HomePanel.QuickLinks)
                        linkSelection = Previous(linkSelection, links.Length);
                    else if (panel == HomePanel.RecentlyPlayed)
                        recentSelection = Previous(recentSelection, _app.RecentlyPlayed.Count);
                    else
                        favoriteSelection = Previous(favoriteSelection, favorites.Count);
                    selectionDirtyPanel = panel;
                    break;
                case ConsoleKey.DownArrow:
                    if (panel == HomePanel.QuickLinks)
                        linkSelection = Next(linkSelection, links.Length);
                    else if (panel == HomePanel.RecentlyPlayed)
                        recentSelection = Next(recentSelection, _app.RecentlyPlayed.Count);
                    else
                        favoriteSelection = Next(favoriteSelection, favorites.Count);
                    selectionDirtyPanel = panel;
                    break;
                case ConsoleKey.LeftArrow:
                    panel = panel == HomePanel.QuickLinks
                        ? HomePanel.RecentFavorites
                        : (HomePanel)((int)panel - 1);
                    contentDirty = true;
                    break;
                case ConsoleKey.RightArrow:
                case ConsoleKey.Tab:
                    panel = panel == HomePanel.RecentFavorites
                        ? HomePanel.QuickLinks
                        : (HomePanel)((int)panel + 1);
                    contentDirty = true;
                    break;
                case ConsoleKey.Enter:
                    if (panel == HomePanel.QuickLinks)
                        return linkSelection;
                    if (panel == HomePanel.RecentlyPlayed && _app.RecentlyPlayed.Count > 0)
                        await StartQueueAsync(_app.RecentlyPlayed, recentSelection, loop: true);
                    else if (panel == HomePanel.RecentFavorites && favorites.Count > 0)
                        await StartQueueAsync(favorites, favoriteSelection, loop: true);
                    contentDirty = true;
                    break;
                case TerminalKeys.Pause:
                    await ResumeOrTogglePauseAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Lyrics:
                    var dashboardLyricsTrack = panel switch
                    {
                        HomePanel.RecentlyPlayed when _app.RecentlyPlayed.Count > 0 =>
                            _app.RecentlyPlayed[recentSelection],
                        HomePanel.RecentFavorites when favorites.Count > 0 => favorites[favoriteSelection],
                        _ => _app.CurrentTrack
                    };
                    if (dashboardLyricsTrack is not null)
                        await _lyricsScreen.ShowAsync(dashboardLyricsTrack);
                    else
                        _status = "Select or play a track before opening lyrics";
                    renderedWidth = -1;
                    break;
                case TerminalKeys.Download:
                    var dashboardDownloadTrack = panel switch
                    {
                        HomePanel.RecentlyPlayed when _app.RecentlyPlayed.Count > 0 =>
                            _app.RecentlyPlayed[recentSelection],
                        HomePanel.RecentFavorites when favorites.Count > 0 => favorites[favoriteSelection],
                        _ => _app.CurrentTrack
                    };
                    await DownloadTrackAsync(dashboardDownloadTrack);
                    renderedWidth = -1;
                    break;
                case ConsoleKey.Oem2:
                    return 0;
                case TerminalKeys.Favorite when panel == HomePanel.QuickLinks && _app.CurrentTrack is not null:
                    await ToggleFavoriteAsync(_app.CurrentTrack);
                    favorites = await SafeFavoritesAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Favorite when panel == HomePanel.RecentFavorites && favorites.Count > 0:
                    await ToggleFavoriteAsync(favorites[favoriteSelection]);
                    favorites = await SafeFavoritesAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Favorite when panel == HomePanel.RecentlyPlayed && _app.RecentlyPlayed.Count > 0:
                    await ToggleFavoriteAsync(_app.RecentlyPlayed[recentSelection]);
                    favorites = await SafeFavoritesAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Shuffle:
                    await ToggleShuffleAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Rewind:
                    await SeekAsync(TimeSpan.FromSeconds(-10));
                    contentDirty = true;
                    break;
                case TerminalKeys.FastForward:
                    await SeekAsync(TimeSpan.FromSeconds(10));
                    contentDirty = true;
                    break;
                case ConsoleKey.MediaNext:
                    await PlayNextAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.NextTrack:
                    await PlayNextAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.PreviousTrack:
                    await PlayPreviousAsync();
                    contentDirty = true;
                    break;
                case ConsoleKey.OemPlus:
                case ConsoleKey.Add:
                    await ChangeVolumeAsync(5);
                    contentDirty = true;
                    break;
                case ConsoleKey.OemMinus:
                case ConsoleKey.Subtract:
                    await ChangeVolumeAsync(-5);
                    contentDirty = true;
                    break;
                case TerminalKeys.Stop:
                    await StopAsync();
                    contentDirty = true;
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return -1;
            }
        }
    }

    private async Task ShowFavoritesAsync()
    {
        while (true)
        {
            var favorites = await SafeFavoritesAsync();
            if (favorites.Count == 0)
            {
                Notice("No favorites saved yet. Press Q to return Home.");
                return;
            }

            var changed = await BrowseTracksAsync("FAVORITES", favorites, true, loopQueue: true);
            if (!changed)
                return;
        }
    }

    private async Task ShowSettingsTransferAsync()
    {
        var selected = 0;
        var options = new[] { "Export all settings", "Import settings", "Back" };
        while (true)
        {
            Clear();
            DrawBox(0, 0, Width(), 11, " BACKUP & RESTORE ");
            WriteAt(2, 2,
                "Includes favorites, recents, player settings, session, lyrics, and local folder.",
                ConsoleColor.DarkGray);
            for (var index = 0; index < options.Length; index++)
                WriteMenuItem(2, 4 + index, Math.Min(36, Width() - 4), options[index], index == selected);
            WriteAt(2, 8, "Import creates a safety backup and then closes Musik.", ConsoleColor.DarkGray);

            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.UpArrow:
                    selected = Previous(selected, options.Length);
                    break;
                case ConsoleKey.DownArrow:
                    selected = Next(selected, options.Length);
                    break;
                case ConsoleKey.Enter when selected == 0:
                    await ExportSettingsAsync();
                    break;
                case ConsoleKey.Enter when selected == 1:
                    if (await ImportSettingsAsync())
                        return;
                    break;
                case ConsoleKey.Enter when selected == 2:
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    private async Task ExportSettingsAsync()
    {
        try
        {
            // Checkpoint the live session before reading the JSON files.
            await _app.SavePlaybackSessionAsync();
            var path = await _settingsTransfer.ExportAsync();
            Notice($"Settings exported to {path}");
        }
        catch (Exception exception)
        {
            Notice($"Export failed: {exception.Message}");
        }
    }

    private async Task<bool> ImportSettingsAsync()
    {
        Clear();
        Console.CursorVisible = true;
        try
        {
            Console.WriteLine("IMPORT MUSIK SETTINGS");
            Console.WriteLine("Paste the exported .json file path, then press Enter. Leave blank to cancel.");
            Console.Write("Backup file: ");
            var path = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(path))
                return false;

            Console.Write("Type IMPORT to replace the current settings: ");
            var confirmation = Console.ReadLine();
            if (!string.Equals(confirmation, "IMPORT", StringComparison.Ordinal))
                return false;

            await _settingsTransfer.ImportAsync(path);
            _importedSettings = true;
            Console.WriteLine();
            Console.WriteLine("Import complete. Musik will close; reopen it to load the restored settings.");
            Console.WriteLine("Press any key to continue.");
            Console.ReadKey(intercept: true);
            return true;
        }
        catch (Exception exception)
        {
            Notice($"Import failed: {exception.Message}");
            return false;
        }
        finally
        {
            Console.CursorVisible = false;
        }
    }

    private async Task ShowLocalMusicAsync()
    {
        var selected = 0;
        var options = new[] { "Browse library", "Change music folder", "Back" };
        while (true)
        {
            DrawLocalMusicMenu(options, selected);
            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.UpArrow:
                    selected = Previous(selected, options.Length);
                    break;
                case ConsoleKey.DownArrow:
                    selected = Next(selected, options.Length);
                    break;
                case ConsoleKey.Enter when selected == 0:
                    IReadOnlyList<Track> tracks;
                    try
                    {
                        tracks = await _localLibrary.ScanAsync();
                    }
                    catch (Exception exception)
                    {
                        _status = $"Could not scan local music: {exception.Message}";
                        tracks = [];
                    }
                    if (tracks.Count == 0)
                    {
                        Notice($"No supported audio files found in {_localLibrary.DirectoryPath}");
                        break;
                    }
                    await BrowseTracksAsync("LOCAL MUSIC", tracks, false, loopQueue: true);
                    break;
                case ConsoleKey.Enter when selected == 1:
                    await ChangeLocalMusicDirectoryAsync();
                    break;
                case ConsoleKey.Enter when selected == 2:
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    private void DrawLocalMusicMenu(IReadOnlyList<string> options, int selected)
    {
        Clear();
        DrawBox(0, 0, Width(), 10, " LOCAL MUSIC ");
        WriteAt(2, 2, Fit($"Folder: {_localLibrary.DirectoryPath}", Width() - 4), ConsoleColor.DarkGray);
        WriteAt(2, 3, "Supports MP3, M4A, WebM, FLAC, WAV, OGG, Opus, and AAC", ConsoleColor.DarkGray);
        for (var index = 0; index < options.Count; index++)
            WriteMenuItem(2, 5 + index, Math.Min(32, Width() - 4), options[index], index == selected);
    }

    private async Task ChangeLocalMusicDirectoryAsync()
    {
        Clear();
        Console.CursorVisible = true;
        try
        {
            Console.WriteLine("LOCAL MUSIC FOLDER");
            Console.WriteLine("Paste or type a folder path, then press Enter. Leave blank to cancel.");
            Console.Write("Folder: ");
            var path = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                await _localLibrary.SetDirectoryAsync(path);
                _status = $"Local music folder changed to {_localLibrary.DirectoryPath}";
            }
            catch (Exception exception)
            {
                _status = $"Could not change music folder: {exception.Message}";
                Notice(_status);
            }
        }
        finally
        {
            Console.CursorVisible = false;
        }
    }

    private async Task<bool> BrowseTracksAsync(
        string title,
        IReadOnlyList<Track> tracks,
        bool removeOnDelete,
        bool collapsible = false,
        bool loopQueue = false)
    {
        if (tracks.Count == 0)
        {
            Notice("This list is empty.");
            return false;
        }

        var selected = 0;
        var renderedWidth = -1;
        var renderedHeight = -1;
        var contentDirty = false;
        var selectionDirty = false;
        var expanded = !collapsible;
        var favoriteIds = (await SafeFavoritesAsync())
            .Select(track => track.Id)
            .ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var visibleCount = expanded ? tracks.Count : Math.Min(5, tracks.Count);
            var visibleTracks = tracks.Take(visibleCount).ToList();
            selected = ClampSelection(selected, visibleTracks.Count);
            var displayTitle = collapsible
                ? $"{title} ({visibleCount}/{tracks.Count})"
                : title;
            if (selectionDirty && renderedWidth >= 0)
            {
                DrawResultRows(visibleTracks, selected, favoriteIds);
                selectionDirty = false;
            }
            else if (contentDirty && renderedWidth >= 0)
            {
                DrawResultsContent(displayTitle, visibleTracks, selected, removeOnDelete, favoriteIds, collapsible);
                contentDirty = false;
            }
            var currentSelection = selected;
            var key = ReadResponsive(
                () => DrawResults(
                    displayTitle,
                    visibleTracks,
                    currentSelection,
                    removeOnDelete,
                    favoriteIds,
                    collapsible),
                ref renderedWidth,
                ref renderedHeight,
                HasFinishedTrack,
                () => DrawNowPlaying(Math.Max(10, Height() - 7), Width()));
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selected = selected == 0 ? visibleTracks.Count - 1 : selected - 1;
                    selectionDirty = true;
                    break;
                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % visibleTracks.Count;
                    selectionDirty = true;
                    break;
                case ConsoleKey.Enter:
                    await StartQueueAsync(tracks, selected, loopQueue);
                    contentDirty = true;
                    break;
                case TerminalKeys.Pause:
                    await ResumeOrTogglePauseAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Lyrics:
                    await _lyricsScreen.ShowAsync(visibleTracks[selected]);
                    renderedWidth = -1;
                    break;
                case TerminalKeys.Download:
                    await DownloadTrackAsync(visibleTracks[selected]);
                    renderedWidth = -1;
                    break;
                case TerminalKeys.AddToPlaylist:
                    await _playlistScreen.AddTrackAsync(visibleTracks[selected]);
                    renderedWidth = -1;
                    break;
                case TerminalKeys.Favorite:
                    var selectedTrack = visibleTracks[selected];
                    await ToggleFavoriteAsync(selectedTrack);
                    if (!favoriteIds.Add(selectedTrack.Id))
                        favoriteIds.Remove(selectedTrack.Id);
                    selectionDirty = true;
                    if (removeOnDelete)
                        return true;
                    break;
                case ConsoleKey.Delete when removeOnDelete:
                case ConsoleKey.Backspace when removeOnDelete:
                    await ToggleFavoriteAsync(visibleTracks[selected]);
                    return true;
                case TerminalKeys.Expand when collapsible:
                    expanded = !expanded;
                    contentDirty = true;
                    renderedWidth = -1;
                    break;
                case TerminalKeys.Shuffle:
                    await ToggleShuffleAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Rewind:
                    await SeekAsync(TimeSpan.FromSeconds(-10));
                    contentDirty = true;
                    break;
                case TerminalKeys.FastForward:
                    await SeekAsync(TimeSpan.FromSeconds(10));
                    contentDirty = true;
                    break;
                case ConsoleKey.MediaNext:
                    await PlayNextAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.NextTrack:
                    await PlayNextAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.PreviousTrack:
                    await PlayPreviousAsync();
                    contentDirty = true;
                    break;
                case ConsoleKey.OemPlus:
                case ConsoleKey.Add:
                    await ChangeVolumeAsync(5);
                    contentDirty = true;
                    break;
                case ConsoleKey.OemMinus:
                case ConsoleKey.Subtract:
                    await ChangeVolumeAsync(-5);
                    contentDirty = true;
                    break;
                case TerminalKeys.Stop:
                    await StopAsync();
                    contentDirty = true;
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return false;
            }
        }
    }

    private void DrawDashboard(
        IReadOnlyList<string> links,
        HomePanel panel,
        int linkSelection,
        int recentSelection,
        int favoriteSelection,
        IReadOnlyList<Track> favorites)
    {
        var width = Width();
        var height = Height();
        var leftWidth = Math.Clamp(width / 4, 22, 30);
        var rightX = leftWidth + 1;
        var rightWidth = width - rightX;
        var contentTop = 3;
        var playerY = Math.Max(17, height - 8);
        var contentHeight = playerY - contentTop;
        var halfHeight = Math.Max(6, contentHeight / 2);
        DrawBox(0, 0, width, 3, "");
        WriteCenteredMixed(1,
            ("M U S I ", ConsoleColor.White),
            ("K . P", ConsoleColor.Cyan),
            (" L A Y E R", ConsoleColor.White));

        DrawBox(0, contentTop, leftWidth, contentHeight, " QUICK LINKS ");

        DrawBox(rightX, contentTop, rightWidth, halfHeight, " RECENTLY PLAYED ");

        var favoriteY = contentTop + halfHeight;
        DrawBox(rightX, favoriteY, rightWidth, contentHeight - halfHeight, " RECENT FAVORITES ");

        DrawBox(0, playerY + 4, width, Math.Max(3, height - playerY - 4), "");
        DrawDashboardContent(
            links, panel, linkSelection, recentSelection, favoriteSelection, favorites);
    }

    private void DrawDashboardContent(
        IReadOnlyList<string> links,
        HomePanel panel,
        int linkSelection,
        int recentSelection,
        int favoriteSelection,
        IReadOnlyList<Track> favorites)
    {
        var width = Width();
        var height = Height();
        var leftWidth = Math.Clamp(width / 4, 22, 30);
        var rightX = leftWidth + 1;
        var rightWidth = width - rightX;
        const int contentTop = 3;
        var playerY = Math.Max(17, height - 8);
        var contentHeight = playerY - contentTop;
        var halfHeight = Math.Max(6, contentHeight / 2);
        var favoriteIds = favorites.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < links.Count; i++)
            WriteMenuItem(2, contentTop + 2 + i, leftWidth - 4, links[i],
                panel == HomePanel.QuickLinks && i == linkSelection);

        DrawTrackPreview(rightX + 2, contentTop + 2, rightWidth - 4, halfHeight - 3,
            _app.RecentlyPlayed, recentSelection, panel == HomePanel.RecentlyPlayed, favoriteIds);

        var favoriteY = contentTop + halfHeight;
        DrawTrackPreview(
            rightX + 2,
            favoriteY + 2,
            rightWidth - 4,
            contentHeight - halfHeight - 3,
            favorites,
            favoriteSelection,
            panel == HomePanel.RecentFavorites,
            favoriteIds);

        DrawNowPlaying(playerY, width);
        WriteAt(
            2,
            playerY + 5,
            $"Play: Enter   Lyrics: L   Download: D   P/N: Previous/Next   Pause: Space   Shuffle: R [{(_playbackQueue.Shuffle ? "ON" : "OFF")}]"
                .PadRight(Math.Max(1, width - 4)),
            ConsoleColor.DarkGray);
        DrawVolumeControl(Math.Min(height - 2, playerY + 6), width);
    }

    private void DrawDashboardPanelContent(
        HomePanel panel,
        IReadOnlyList<string> links,
        int linkSelection,
        int recentSelection,
        int favoriteSelection,
        IReadOnlyList<Track> favorites)
    {
        var width = Width();
        var height = Height();
        var leftWidth = Math.Clamp(width / 4, 22, 30);
        var rightX = leftWidth + 1;
        var rightWidth = width - rightX;
        const int contentTop = 3;
        var playerY = Math.Max(17, height - 8);
        var contentHeight = playerY - contentTop;
        var halfHeight = Math.Max(6, contentHeight / 2);
        var favoriteIds = favorites.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);

        switch (panel)
        {
            case HomePanel.QuickLinks:
                for (var index = 0; index < links.Count; index++)
                    WriteMenuItem(
                        2,
                        contentTop + 2 + index,
                        leftWidth - 4,
                        links[index],
                        index == linkSelection);
                break;
            case HomePanel.RecentlyPlayed:
                DrawTrackPreview(
                    rightX + 2,
                    contentTop + 2,
                    rightWidth - 4,
                    halfHeight - 3,
                    _app.RecentlyPlayed,
                    recentSelection,
                    true,
                    favoriteIds);
                break;
            case HomePanel.RecentFavorites:
                DrawTrackPreview(
                    rightX + 2,
                    contentTop + halfHeight + 2,
                    rightWidth - 4,
                    contentHeight - halfHeight - 3,
                    favorites,
                    favoriteSelection,
                    true,
                    favoriteIds);
                break;
        }
    }

    private void DrawResults(
        string title,
        IReadOnlyList<Track> tracks,
        int selected,
        bool removeOnDelete,
        IReadOnlySet<string> favoriteIds,
        bool collapsible)
    {
        var width = Width();
        var height = Height();
        var listBottom = Math.Max(10, height - 7);

        DrawBox(0, 0, width, listBottom, $" {title.ToUpperInvariant()} ");
        WriteAt(2, 2, "TYPE   TITLE / ARTIST", ConsoleColor.DarkGray);
        WriteAt(Math.Max(20, width - 12), 2, "DURATION", ConsoleColor.DarkGray);
        DrawResultsContent(title, tracks, selected, removeOnDelete, favoriteIds, collapsible);
    }

    private void DrawResultsContent(
        string title,
        IReadOnlyList<Track> tracks,
        int selected,
        bool removeOnDelete,
        IReadOnlySet<string> favoriteIds,
        bool collapsible)
    {
        var width = Width();
        var height = Height();
        var listBottom = Math.Max(10, height - 7);
        DrawResultRows(tracks, selected, favoriteIds);

        DrawNowPlaying(listBottom, width);
        var favoriteHint = removeOnDelete ? "F/Delete: Remove" : "F: Favorite";
        var expandHint = collapsible ? "   E: Expand/Collapse" : "";
        WriteAt(2, Math.Min(height - 1, listBottom + 5),
            $"Arrows: Navigate   Enter: Play   A: Playlist   L: Lyrics   D: Download   P/N: Previous/Next   " +
            $"{favoriteHint}{expandHint}   R: Shuffle [{(_playbackQueue.Shuffle ? "ON" : "OFF")}]   Q: Home",
            ConsoleColor.DarkGray);
    }

    private static void DrawResultRows(
        IReadOnlyList<Track> tracks,
        int selected,
        IReadOnlySet<string> favoriteIds)
    {
        var width = Width();
        var height = Height();
        var listBottom = Math.Max(10, height - 7);
        var rows = Math.Max(1, listBottom - 5);
        var first = Math.Clamp(selected - rows / 2, 0, Math.Max(0, tracks.Count - rows));
        var last = Math.Min(tracks.Count, first + rows);

        for (var index = first; index < last; index++)
        {
            var track = tracks[index];
            var y = 4 + index - first;
            var favorite = favoriteIds.Contains(track.Id) ? "♥" : " ";
            var text = $"{(index == selected ? ">" : " ")} {index + 1,2}. {favorite} SONG   {track.Title} - {track.Artist}";
            WriteAt(1, y, Fit(text, width - 13).PadRight(Math.Max(1, width - 13)),
                index == selected ? ConsoleColor.Black : ConsoleColor.Gray,
                index == selected ? ConsoleColor.Blue : ConsoleColor.Black);
            WriteAt(width - 11, y, track.DurationText.PadLeft(8),
                index == selected ? ConsoleColor.Yellow : ConsoleColor.DarkCyan,
                index == selected ? ConsoleColor.Blue : ConsoleColor.Black);
        }
    }

    private void DrawNowPlaying(int y, int width)
    {
        DrawBox(0, y, width, 4, "");
        var track = _app.CurrentTrack;
        var title = track is null
            ? _status
            : $"{(_app.IsPaused ? "[PAUSED] " : "")}{track.Title}";
        var elapsed = _app.Elapsed;
        var duration = track?.Duration;
        var times = duration is null
            ? $"{FormatTime(elapsed)} / --:--"
            : $"{FormatTime(elapsed)} / {FormatTime(duration.Value)}";

        WriteAt(
            2,
            y + 1,
            Fit($"> Currently playing: {title}", width - times.Length - 6),
            _app.IsPlaying ? ConsoleColor.Cyan : ConsoleColor.DarkGray);
        WriteAt(Math.Max(2, width - times.Length - 2), y + 1, times, ConsoleColor.Gray);

        var barWidth = Math.Max(10, width - 4);
        var progress = duration is { TotalSeconds: > 0 }
            ? Math.Clamp(elapsed.TotalSeconds / duration.Value.TotalSeconds, 0, 1)
            : 0;
        var filled = (int)(barWidth * progress);
        if (filled > 0)
            WriteAt(2, y + 2, new string(' ', filled), ConsoleColor.Blue, ConsoleColor.Blue);
        if (filled < barWidth)
            WriteAt(2 + filled, y + 2, new string('░', barWidth - filled), ConsoleColor.DarkGray);
    }

    private static void DrawTrackPreview(
        int x,
        int y,
        int width,
        int height,
        IReadOnlyList<Track> tracks,
        int selected,
        bool focused,
        IReadOnlySet<string> favoriteIds)
    {
        var rows = Math.Max(1, height);
        if (tracks.Count == 0)
        {
            WriteAt(x, y, "No tracks yet".PadRight(Math.Max(1, width)), ConsoleColor.DarkGray);
            for (var row = 1; row < rows; row++)
                WriteAt(x, y + row, new string(' ', Math.Max(1, width)), ConsoleColor.Gray);
            return;
        }

        var first = Math.Clamp(selected - rows / 2, 0, Math.Max(0, tracks.Count - rows));
        var last = Math.Min(tracks.Count, first + rows);
        for (var index = first; index < last; index++)
        {
            var active = focused && index == selected;
            WriteAt(
                x,
                y + index - first,
                Fit($"{(active ? ">" : " ")} {(favoriteIds.Contains(tracks[index].Id) ? "♥" : " ")} " +
                    $"{tracks[index].Title} - {tracks[index].Artist}", width).PadRight(width),
                active ? ConsoleColor.Black : ConsoleColor.Gray,
                active ? ConsoleColor.Cyan : ConsoleColor.Black);
        }

        var renderedRows = last - first;
        for (var row = renderedRows; row < rows; row++)
            WriteAt(x, y + row, new string(' ', Math.Max(1, width)), ConsoleColor.Gray);
    }

    private void DrawVolumeControl(int y, int width)
    {
        const string label = "Volume  - / +";
        const int preferredBarWidth = 20;
        WriteAt(2, y, label, ConsoleColor.Cyan);

        var barX = label.Length + 4;
        var available = Math.Max(4, width - barX - 8);
        var barWidth = Math.Min(preferredBarWidth, available);
        var filled = (int)Math.Round(barWidth * _app.Volume / 100d);

        if (filled > 0)
            WriteAt(barX, y, new string(' ', filled), ConsoleColor.Blue, ConsoleColor.Blue);
        if (filled < barWidth)
            WriteAt(barX + filled, y, new string('░', barWidth - filled), ConsoleColor.DarkGray);

        WriteAt(barX + barWidth + 2, y, $"{_app.Volume,3}%", ConsoleColor.Cyan);
    }

    private async Task StartQueueAsync(
        IReadOnlyList<Track> tracks,
        int selected,
        bool loop)
    {
        try
        {
            var track = await _playbackQueue.StartAsync(tracks, selected, loop);
            _status = $"Playing: {track.Title}";
        }
        catch (Exception exception)
        {
            _status = $"Playback failed: {exception.Message}";
        }
    }

    private async Task PlayNextAsync()
    {
        try
        {
            var track = await _playbackQueue.NextAsync();
            _status = track is null ? "Queue finished" : $"Playing next: {track.Title}";
        }
        catch (Exception exception)
        {
            _status = $"Could not play next track: {exception.Message}";
        }
    }

    private async Task PlayPreviousAsync()
    {
        try
        {
            var track = await _playbackQueue.PreviousAsync();
            _status = track is null ? "No active queue" : $"Playing previous: {track.Title}";
        }
        catch (Exception exception)
        {
            _status = $"Could not play previous track: {exception.Message}";
        }
    }

    private bool HasFinishedTrack() =>
        _playbackQueue.HasFinishedCurrent;

    private async Task ToggleShuffleAsync()
    {
        var enabled = _playbackQueue.ToggleShuffle();
        await _app.SetShuffleAsync(enabled);
        _status = $"Shuffle {(enabled ? "enabled" : "disabled")}";
    }

    private async Task SeekAsync(TimeSpan offset)
    {
        if (!_app.IsPlaying)
        {
            _status = "Select a track before seeking";
            return;
        }

        _app.Seek(offset);
        await _app.SavePlaybackSessionAsync();
        _status = offset < TimeSpan.Zero ? "Rewound 10 seconds" : "Forwarded 10 seconds";
    }

    private async Task ToggleFavoriteAsync(Track track)
    {
        try
        {
            var added = await _app.ToggleFavoriteAsync(track);
            _status = added ? $"Added favorite: {track.Title}" : $"Removed favorite: {track.Title}";
        }
        catch (Exception exception)
        {
            _status = $"Favorite update failed: {exception.Message}";
        }
    }

    private async Task DownloadTrackAsync(Track? track)
    {
        if (track is null)
        {
            _status = "Select or play a track before downloading";
            return;
        }
        if (track.IsLocal)
        {
            _status = "This track is already stored locally";
            return;
        }

        Clear();
        DrawBox(0, 0, Width(), 7, " DOWNLOAD ");
        WriteAt(2, 2, Fit($"Downloading: {track.Title} - {track.Artist}", Width() - 4), ConsoleColor.Cyan);
        var progress = new InlineProgress(value =>
        {
            var percent = Math.Clamp((int)Math.Round(value * 100), 0, 100);
            WriteAt(2, 4, $"Progress: {percent,3}%".PadRight(20), ConsoleColor.Gray);
        });

        try
        {
            var result = await _downloads.DownloadAsync(track, progress);
            _status = result.AlreadyExisted
                ? $"Already downloaded: {Path.GetFileName(result.FilePath)}"
                : $"Downloaded: {Path.GetFileName(result.FilePath)}";
        }
        catch (Exception exception)
        {
            _status = $"Download failed: {exception.Message}";
        }
    }

    private async Task<IReadOnlyList<Track>> SafeFavoritesAsync()
    {
        try
        {
            return await _app.GetFavoritesAsync();
        }
        catch (Exception exception)
        {
            _status = $"Could not load favorites: {exception.Message}";
            return [];
        }
    }

    private async Task ChangeVolumeAsync(int delta)
    {
        await _app.ChangeVolumeAsync(delta);
        _status = $"Volume: {_app.Volume}%";
    }

    private async Task StopAsync()
    {
        await _app.SavePlaybackSessionAsync();
        _playbackQueue.Stop();
        _status = "Playback stopped";
    }

    private async Task ResumeOrTogglePauseAsync()
    {
        if (!_app.IsPlaying)
        {
            try
            {
                if (await _app.ResumePreviousSessionAsync())
                    _status = $"Resumed: {_app.CurrentTrack?.Title}";
                else
                    _status = "No previous session is available";
            }
            catch (Exception exception)
            {
                _status = $"Could not resume session: {exception.Message}";
            }
            return;
        }

        _app.TogglePause();
        _status = _app.IsPaused ? "Playback paused" : "Playback resumed";
        await _app.SavePlaybackSessionAsync();
    }

    private static int Previous(int selected, int count) =>
        count <= 0 ? 0 : selected == 0 ? count - 1 : selected - 1;

    private static int Next(int selected, int count) =>
        count <= 0 ? 0 : (selected + 1) % count;

    private static int ClampSelection(int selected, int count) =>
        count <= 0 ? 0 : Math.Clamp(selected, 0, count - 1);

    private static void Notice(string message)
    {
        Clear();
        DrawBox(0, 0, Width(), 6, " NOTICE ");
        WriteAt(2, 2, Fit(message, Width() - 4), ConsoleColor.Yellow);
        WriteAt(2, 4, "Press any key to continue", ConsoleColor.DarkGray);
        Console.ReadKey(true);
    }

    private static void DrawBox(int x, int y, int width, int height, string title)
    {
        if (width < 4 || height < 2)
            return;

        var horizontal = new string('─', width - 2);
        WriteAt(x, y, $"┌{horizontal}┐", ConsoleColor.Blue);
        for (var row = 1; row < height - 1; row++)
        {
            WriteAt(x, y + row, "│", ConsoleColor.Blue);
            WriteAt(x + width - 1, y + row, "│", ConsoleColor.Blue);
        }
        WriteAt(x, y + height - 1, $"└{horizontal}┘", ConsoleColor.Blue);
        if (!string.IsNullOrEmpty(title))
            WriteAt(x + 2, y, Fit(title, width - 4), ConsoleColor.Cyan);
    }

    private static void WriteMenuItem(int x, int y, int width, string text, bool selected) =>
        WriteAt(x, y, Fit($"{(selected ? ">" : " ")} {text}", width).PadRight(width),
            selected ? ConsoleColor.Black : ConsoleColor.Gray,
            selected ? ConsoleColor.Cyan : ConsoleColor.Black);

    private static void WriteCenteredMixed(
        int row,
        params (string text, ConsoleColor color)[] segments)
    {
        var fullTextLength = segments.Sum(segment => segment.text.Length);
        var column = Math.Max(0, (Width() - fullTextLength) / 2);
        foreach (var (text, color) in segments)
        {
            WriteAt(column, row, text, color);
            column += text.Length;
        }
    }

    private static void WriteCentered(int y, string text, ConsoleColor color) =>
        WriteAt(Math.Max(0, (Width() - text.Length) / 2), y, text, color);

    private static void WriteAt(
        int x,
        int y,
        string text,
        ConsoleColor foreground,
        ConsoleColor background = ConsoleColor.Black) =>
        TerminalCanvas.WriteAt(x, y, text, foreground, background);

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        if (text.Length <= width)
            return text;
        return width <= 3 ? text[..width] : string.Concat(text.AsSpan(0, width - 3), "...");
    }

    private static int Width() => Math.Max(1, Console.WindowWidth - 1);
    private static int Height() => Math.Max(1, Console.WindowHeight);

    private enum HomePanel
    {
        QuickLinks,
        RecentlyPlayed,
        RecentFavorites
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
