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
    private readonly PlaybackPanel _playbackPanel = new();
    private readonly SpectrumPanel _spectrumPanel = new();
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
        _playbackQueue.StatusChanged += status => _status = status;
        if (Enum.TryParse<TerminalTheme>(app.ThemeName, true, out var savedTheme))
            ThemeManager.Apply(savedTheme);
        _searchScreen = new SearchScreen(
            app,
            (title, tracks) => BrowseTracksAsync(title, tracks, false, collapsible: true),
            status => _status = status);
        _playlistScreen = new PlaylistScreen(
            app,
            playlists,
            BrowsePlaylistAsync,
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
        _playbackQueue.StartAutomaticPlayback();
        try
        {
            while (true)
            {
                var selected = await ShowDashboardAsync();
                if (selected is -1 or 9)
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
                    await new SpectrumScreen(_app).ShowAsync();
                else if (selected == 6)
                    await ShowLocalMusicAsync();
                else if (selected == 7)
                    await ShowSettingsTransferAsync();
                else if (selected == 8)
                    GuideScreen.Show();

                if (_importedSettings)
                    return;
            }
        }
        finally
        {
            await _playbackQueue.DisposeAsync();
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
        var favoriteSelection = 0;
        var links = new[]
        {
            "Search", "Playlists", "Favorites", "Recently Played", "Themes",
            "Spectrum", "Local Music", "Backup & Restore", "Keyboard Guide", "Quit"
        };
        var renderedWidth = -1;
        var renderedHeight = -1;
        var contentDirty = false;
        HomePanel? selectionDirtyPanel = null;
        var favorites = await SafeFavoritesAsync();
        var lastPlaybackRedraw = 0L;

        while (true)
        {
            favoriteSelection = ClampSelection(favoriteSelection, favorites.Count);
            if (panel == HomePanel.RecentFavorites && favorites.Count > 0)
                _ = _app.WarmSelectionAsync(favorites[favoriteSelection]);
            if (selectionDirtyPanel is not null && renderedWidth >= 0)
            {
                DrawDashboardPanelContent(
                    selectionDirtyPanel.Value,
                    links,
                    linkSelection,
                    favoriteSelection,
                    favorites);
                selectionDirtyPanel = null;
            }
            else if (contentDirty && renderedWidth >= 0 && TerminalLayout.Fits(Width(), Height()))
            {
                DrawDashboardContent(
                    links, panel, linkSelection, favoriteSelection, favorites);
                contentDirty = false;
            }
            var currentPanel = panel;
            var currentLink = linkSelection;
            var currentFavorite = favoriteSelection;
            var key = ReadResponsive(
                () => DrawDashboard(
                    links, currentPanel, currentLink, currentFavorite, favorites),
                ref renderedWidth,
                ref renderedHeight,
                HasFinishedTrack,
                () =>
                {
                    if (Environment.TickCount64 - lastPlaybackRedraw >= 250)
                    {
                        DrawNowPlaying(TerminalLayout.PlayerTop(Height()), Width());
                        DrawStatus(TerminalLayout.PlayerTop(Height()) + 5, Width());
                        lastPlaybackRedraw = Environment.TickCount64;
                    }
                    DrawDashboardSpectrum();
                    if (currentPanel == HomePanel.RecentFavorites && favorites.Count > 0)
                        _ = _app.WarmSelectionAsync(favorites[ClampSelection(currentFavorite, favorites.Count)]);
                }, redrawIntervalMilliseconds: 16);

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (panel == HomePanel.QuickLinks)
                        linkSelection = Previous(linkSelection, links.Length);
                    else
                        favoriteSelection = Previous(favoriteSelection, favorites.Count);
                    selectionDirtyPanel = panel;
                    break;
                case ConsoleKey.DownArrow:
                    if (panel == HomePanel.QuickLinks)
                        linkSelection = Next(linkSelection, links.Length);
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
                    var panelDirection = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
                    panel = (HomePanel)(((int)panel + 2 + panelDirection) % 2);
                    contentDirty = true;
                    break;
                case ConsoleKey.Enter:
                    if (panel == HomePanel.QuickLinks)
                        return linkSelection;
                    if (panel == HomePanel.RecentFavorites && favorites.Count > 0)
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
                        HomePanel.RecentFavorites when favorites.Count > 0 => favorites[favoriteSelection],
                        _ => _app.CurrentTrack
                    };
                    await DownloadTrackAsync(dashboardDownloadTrack);
                    renderedWidth = -1;
                    break;
                case ConsoleKey.Oem2:
                    return 0;
                case TerminalKeys.TrackDetails:
                    var detailsTrack = DashboardTrack(panel, favoriteSelection, favorites);
                    if (detailsTrack is not null)
                        DocumentScreen.ShowTrack(detailsTrack);
                    else
                        _status = "Select or play a song to see its details";
                    renderedWidth = -1;
                    break;
                case TerminalKeys.AddToPlaylist:
                    var playlistTrack = DashboardTrack(panel, favoriteSelection, favorites);
                    if (playlistTrack is not null)
                        await _playlistScreen.AddTrackAsync(playlistTrack);
                    else
                        _status = "Select or play a song to add it to a playlist";
                    renderedWidth = -1;
                    break;
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

    private Track? DashboardTrack(HomePanel panel, int favorite, IReadOnlyList<Track> favorites) =>
        panel switch
        {
            HomePanel.RecentFavorites when favorites.Count > 0 => favorites[favorite],
            _ => _app.CurrentTrack
        };

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

            switch (ReadWithHelp().Key)
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

            _playbackQueue.Stop();
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
            switch (ReadWithHelp().Key)
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

    private Task BrowsePlaylistAsync(SavedPlaylist playlist) => BrowseTracksAsync(
        $"PLAYLIST: {playlist.Name}", playlist.Tracks, false, loopQueue: true,
        removeTrack: index => _playlistScreen.RemoveTrackAsync(playlist.Id, index),
        queueSourceId: playlist.Id);

    private async Task<bool> BrowseTracksAsync(
        string title,
        IReadOnlyList<Track> tracks,
        bool removeOnDelete,
        bool collapsible = false,
        bool loopQueue = false,
        Func<int, Task>? removeTrack = null,
        string? queueSourceId = null)
    {
        if (tracks.Count == 0 && removeTrack is null)
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
                DrawStatus(TerminalLayout.PlayerTop(Height()) + 5, Width());
                selectionDirty = false;
            }
            else if (contentDirty && renderedWidth >= 0 && TerminalLayout.Fits(Width(), Height()))
            {
                DrawResultsContent(displayTitle, visibleTracks, selected, removeOnDelete, favoriteIds, collapsible,
                    removeTrack is not null);
                contentDirty = false;
            }
            var currentSelection = selected;
            if (visibleTracks.Count > 0) _ = _app.WarmSelectionAsync(visibleTracks[selected]);
            var key = ReadResponsive(
                () => DrawResults(
                    displayTitle,
                    visibleTracks,
                    currentSelection,
                    removeOnDelete,
                    favoriteIds,
                    collapsible,
                    removeTrack is not null),
                ref renderedWidth,
                ref renderedHeight,
                HasFinishedTrack,
                () =>
                {
                    DrawNowPlaying(TerminalLayout.PlayerTop(Height()), Width());
                    if (visibleTracks.Count > 0) _ = _app.WarmSelectionAsync(visibleTracks[currentSelection]);
                });
            switch (key.Key)
            {
                case ConsoleKey.PageUp:
                case ConsoleKey.PageDown:
                    selected = ListNavigation.Page(selected, visibleTracks.Count, TerminalLayout.SongRows(Height()),
                        key.Key == ConsoleKey.PageUp ? -1 : 1);
                    selectionDirty = true;
                    break;
                case ConsoleKey.Home:
                case ConsoleKey.End:
                    selected = key.Key == ConsoleKey.Home ? 0 : Math.Max(0, visibleTracks.Count - 1);
                    selectionDirty = true;
                    break;
                case TerminalKeys.TrackDetails when visibleTracks.Count > 0:
                    DocumentScreen.ShowTrack(visibleTracks[selected]);
                    renderedWidth = -1;
                    break;
                case ConsoleKey.Oem2 when collapsible:
                    return false;
                case ConsoleKey.Oem2:
                    await _searchScreen.ShowAsync();
                    renderedWidth = -1;
                    break;
                case ConsoleKey.UpArrow when visibleTracks.Count > 0:
                    selected = selected == 0 ? visibleTracks.Count - 1 : selected - 1;
                    selectionDirty = true;
                    break;
                case ConsoleKey.DownArrow when visibleTracks.Count > 0:
                    selected = (selected + 1) % visibleTracks.Count;
                    selectionDirty = true;
                    break;
                case ConsoleKey.Enter when visibleTracks.Count > 0:
                    await StartQueueAsync(tracks, selected, loopQueue, queueSourceId);
                    contentDirty = true;
                    break;
                case TerminalKeys.Pause:
                    await ResumeOrTogglePauseAsync();
                    contentDirty = true;
                    break;
                case TerminalKeys.Lyrics when visibleTracks.Count > 0:
                    await _lyricsScreen.ShowAsync(visibleTracks[selected]);
                    renderedWidth = -1;
                    break;
                case TerminalKeys.Download when visibleTracks.Count > 0:
                    await DownloadTrackAsync(visibleTracks[selected]);
                    renderedWidth = -1;
                    break;
                case TerminalKeys.AddToPlaylist when visibleTracks.Count > 0:
                    await _playlistScreen.AddTrackAsync(visibleTracks[selected]);
                    renderedWidth = -1;
                    break;
                case TerminalKeys.Favorite when visibleTracks.Count > 0:
                    var selectedTrack = visibleTracks[selected];
                    await ToggleFavoriteAsync(selectedTrack);
                    if (!favoriteIds.Add(selectedTrack.Id))
                        favoriteIds.Remove(selectedTrack.Id);
                    selectionDirty = true;
                    if (removeOnDelete)
                        return true;
                    break;
                case ConsoleKey.Delete when removeTrack is not null && visibleTracks.Count > 0:
                case ConsoleKey.Backspace when removeTrack is not null && visibleTracks.Count > 0:
                    try
                    {
                        await removeTrack(selected);
                        _playbackQueue.RemoveTrack(queueSourceId!, selected);
                        tracks = tracks.Where((_, index) => index != selected).ToList();
                        contentDirty = true;
                    }
                    catch (Exception exception)
                    {
                        Notice($"Could not remove track: {exception.Message}");
                        renderedWidth = -1;
                    }
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
        int favoriteSelection,
        IReadOnlyList<Track> favorites)
    {
        var width = Width();
        var height = Height();
        var leftWidth = Math.Clamp(width / 4, 22, 30);
        var rightX = leftWidth + 1;
        var rightWidth = width - rightX;
        var contentTop = 3;
        var playerY = TerminalLayout.PlayerTop(height);
        var contentHeight = playerY - contentTop;
        var halfHeight = Math.Max(4, contentHeight / 2);
        DrawBox(0, 0, width, 3, "");
        WriteCenteredMixed(1,
            ("M U S I ", ConsoleColor.White),
            ("K . P", ConsoleColor.Cyan),
            (" L A Y E R", ConsoleColor.White));

        DrawBox(0, contentTop, leftWidth, contentHeight, " QUICK LINKS ");

        DrawBox(rightX, contentTop, rightWidth, halfHeight, " RECENT FAVORITES ");

        var favoriteY = contentTop + halfHeight;
        DrawBox(rightX, favoriteY, rightWidth, contentHeight - halfHeight, "");

        DrawBox(0, playerY + 4, width, Math.Max(3, height - playerY - 4), "");
        DrawDashboardContent(
            links, panel, linkSelection, favoriteSelection, favorites);
    }

    private void DrawDashboardContent(
        IReadOnlyList<string> links,
        HomePanel panel,
        int linkSelection,
        int favoriteSelection,
        IReadOnlyList<Track> favorites)
    {
        var width = Width();
        var height = Height();
        var leftWidth = Math.Clamp(width / 4, 22, 30);
        var rightX = leftWidth + 1;
        var rightWidth = width - rightX;
        const int contentTop = 3;
        var playerY = TerminalLayout.PlayerTop(height);
        var contentHeight = playerY - contentTop;
        var halfHeight = Math.Max(4, contentHeight / 2);
        var favoriteIds = favorites.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);

        DrawQuickLinks(links, linkSelection, panel == HomePanel.QuickLinks, leftWidth, contentHeight);

        DrawTrackPreview(rightX + 2, contentTop + 2, rightWidth - 4, halfHeight - 3,
            favorites, favoriteSelection, panel == HomePanel.RecentFavorites, favoriteIds);
        DrawDashboardSpectrum();

        DrawNowPlaying(playerY, width);
        DrawStatus(playerY + 5, width);
        WriteAt(2, playerY + 6, TerminalText.PadRight(panel == HomePanel.QuickLinks
            ? "Enter: Open  Tab: Panels  /: Search  F1: Guide  Q: Quit"
            : "Enter: Play  F: Fav  A: Add  I: Info  F1: Guide  Q: Quit", width - 4), ConsoleColor.Cyan);
        DrawVolumeControl(playerY + 7, width);
    }

    private void DrawDashboardPanelContent(
        HomePanel panel,
        IReadOnlyList<string> links,
        int linkSelection,
        int favoriteSelection,
        IReadOnlyList<Track> favorites)
    {
        var width = Width();
        var height = Height();
        var leftWidth = Math.Clamp(width / 4, 22, 30);
        var rightX = leftWidth + 1;
        var rightWidth = width - rightX;
        const int contentTop = 3;
        var playerY = TerminalLayout.PlayerTop(height);
        var contentHeight = playerY - contentTop;
        var halfHeight = Math.Max(4, contentHeight / 2);
        var favoriteIds = favorites.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);

        switch (panel)
        {
            case HomePanel.QuickLinks:
                DrawQuickLinks(links, linkSelection, true, leftWidth, contentHeight);
                break;
            case HomePanel.RecentFavorites:
                DrawTrackPreview(
                    rightX + 2,
                    contentTop + 2,
                    rightWidth - 4,
                    halfHeight - 3,
                    favorites,
                    favoriteSelection,
                    true,
                    favoriteIds);
                break;
        }
    }

    private void DrawDashboardSpectrum()
    {
        var leftWidth = Math.Clamp(Width() / 4, 22, 30);
        var rightX = leftWidth + 1;
        var contentHeight = TerminalLayout.PlayerTop(Height()) - 3;
        var halfHeight = Math.Max(4, contentHeight / 2);
        var style = Enum.TryParse<SpectrumStyle>(_app.AudioPreferences.Spectrum, out var saved) ? saved : SpectrumStyle.CatOutline;
        _spectrumPanel.Draw(rightX + 1, 4 + halfHeight, Width() - rightX - 2,
            contentHeight - halfHeight - 2, style, _app.Spectrum);
    }

    private static void DrawQuickLinks(IReadOnlyList<string> links, int selected, bool focused, int width, int height)
    {
        var rows = Math.Max(1, height - 2);
        var first = ListNavigation.FirstVisible(selected, links.Count, rows);
        for (var row = 0; row < rows; row++)
        {
            var index = first + row;
            WriteMenuItem(2, 4 + row, width - 4, index < links.Count ? links[index] : "",
                focused && index == selected);
        }
    }

    private void DrawStatus(int y, int width) =>
        WriteAt(2, y, TerminalText.PadRight(_status, width - 4), ConsoleColor.Gray);

    private void DrawResults(
        string title,
        IReadOnlyList<Track> tracks,
        int selected,
        bool removeOnDelete,
        IReadOnlySet<string> favoriteIds,
        bool collapsible,
        bool playlistRemoval = false)
    {
        var width = Width();
        var height = Height();
        var listBottom = TerminalLayout.PlayerTop(height);

        DrawBox(0, 0, width, listBottom, $" {title.ToUpperInvariant()} ");
        WriteAt(2, 2, "TYPE   TITLE / ARTIST", ConsoleColor.DarkGray);
        WriteAt(Math.Max(20, width - 12), 2, "DURATION", ConsoleColor.DarkGray);
        DrawResultsContent(title, tracks, selected, removeOnDelete, favoriteIds, collapsible, playlistRemoval);
    }

    private void DrawResultsContent(
        string title,
        IReadOnlyList<Track> tracks,
        int selected,
        bool removeOnDelete,
        IReadOnlySet<string> favoriteIds,
        bool collapsible,
        bool playlistRemoval = false)
    {
        var width = Width();
        var height = Height();
        var listBottom = TerminalLayout.PlayerTop(height);
        DrawResultRows(tracks, selected, favoriteIds);

        DrawNowPlaying(listBottom, width);
        DrawBox(0, listBottom + 4, width, 5, "");
        DrawStatus(listBottom + 5, width);
        var contextHint = playlistRemoval || removeOnDelete ? "Del:Remove " : collapsible ? "E:More " : "";
        WriteAt(2, listBottom + 6, TerminalText.PadRight(
            $"{contextHint}Enter:Play F:Fav A:Add I:Info F1:Guide", width - 4), ConsoleColor.Cyan);
        WriteAt(2, listBottom + 7, TerminalText.PadRight(width >= 100
            ? "Space: Pause  N/P: Next/Prev  L: Lyrics  D: Download  +/-: Volume  /: Search  Q: Back"
            : "Space: Pause  N/P: Next/Prev  +/-: Vol  Q: Back", width - 4), ConsoleColor.DarkGray);
    }

    private static void DrawResultRows(
        IReadOnlyList<Track> tracks,
        int selected,
        IReadOnlySet<string> favoriteIds)
    {
        var width = Width();
        var height = Height();
        var listBottom = TerminalLayout.PlayerTop(height);
        var rows = TerminalLayout.SongRows(height);
        var first = Math.Clamp(selected - rows / 2, 0, Math.Max(0, tracks.Count - rows));
        var last = Math.Min(tracks.Count, first + rows);

        for (var index = first; index < last; index++)
        {
            var track = tracks[index];
            var y = 4 + index - first;
            var favorite = favoriteIds.Contains(track.Id) ? "♥" : " ";
            var prefix = $"{(index == selected ? ">" : " ")} {index + 1,2}. {favorite} SONG   ";
            var text = prefix + TerminalText.TrackLabel(track.Title, track.Artist,
                width - 13 - TerminalText.Width(prefix));
            WriteAt(1, y, TerminalText.PadRight(text, width - 13),
                index == selected ? ConsoleColor.Black : ConsoleColor.Gray,
                index == selected ? ConsoleColor.Blue : ConsoleColor.Black);
            WriteAt(width - 11, y, track.DurationText.PadLeft(8),
                index == selected ? ConsoleColor.Yellow : ConsoleColor.DarkCyan,
                index == selected ? ConsoleColor.Blue : ConsoleColor.Black);
        }

        // Deletions and shorter lists must erase rows left by the previous frame.
        for (var row = last - first; row < rows; row++)
            WriteAt(1, 4 + row, new string(' ', Math.Max(0, width - 2)), ConsoleColor.Gray);
        if (tracks.Count == 0)
            WriteAt(2, 4, Fit("This playlist is empty. Add songs with A from Search or Local Music.", width - 4),
                ConsoleColor.DarkGray);
        var range = tracks.Count == 0 ? "0 songs" : $"{first + 1}-{last} / {tracks.Count}";
        WriteAt(2, 2, TerminalText.PadRight($"TITLE / ARTIST   [{range}]", width - 16), ConsoleColor.DarkGray);
    }

    private void DrawNowPlaying(int y, int width)
    {
        var state = _app.IsPaused ? "PAUSED" : _app.IsPlaying ? "PLAYING" : "READY";
        var heading = $" {state} | Vol {_app.Volume}% | Shuffle {(_playbackQueue.Shuffle ? "ON" : "OFF")} ";
        var track = _app.CurrentTrack;
        var title = track is null
            ? _status
            : $"{(_app.IsPaused ? "[PAUSED] " : "")}{track.Title}";
        var elapsed = _app.Elapsed;
        var duration = track?.Duration;
        var times = duration is null
            ? $"{FormatTime(elapsed)} / --:--"
            : $"{FormatTime(elapsed)} / {FormatTime(duration.Value)}";

        var barWidth = Math.Max(0, width - 4);
        var progress = duration is { TotalSeconds: > 0 }
            ? Math.Clamp(elapsed.TotalSeconds / duration.Value.TotalSeconds, 0, 1)
            : 0;
        var filled = (int)(barWidth * progress);
        _playbackPanel.Draw(new PlaybackPanelFrame(y, width, TerminalCanvas.Version, ThemeManager.Current,
            heading, title, times, _app.IsPlaying, filled));
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
            WriteAt(x, y, TerminalText.PadRight("No tracks yet", width), ConsoleColor.DarkGray);
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
                TerminalText.PadRight(
                    $"{(active ? ">" : " ")} {(favoriteIds.Contains(tracks[index].Id) ? "♥" : " ")} " +
                    TerminalText.TrackLabel(tracks[index].Title, tracks[index].Artist, width - 4), width),
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
        bool loop,
        string? sourceId = null)
    {
        try
        {
            var track = await _playbackQueue.StartAsync(tracks, selected, loop, sourceId);
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
        WriteAt(x, y, TerminalText.PadRight($"{(selected ? ">" : " ")} {text}", width),
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

    private static string Fit(string text, int width) => TerminalCanvas.Fit(text, width);

    private static int Width() => Math.Max(1, Console.WindowWidth - 1);
    private static int Height() => Math.Max(1, Console.WindowHeight);

    private enum HomePanel
    {
        QuickLinks,
        RecentFavorites
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
