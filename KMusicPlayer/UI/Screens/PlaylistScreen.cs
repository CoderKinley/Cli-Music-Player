using System.Text;
using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.UI.Screens;

public sealed class PlaylistScreen
{
    private readonly MusicApplication _music;
    private readonly IPlaylistRepository _playlists;
    private readonly Func<string, IReadOnlyList<Track>, Task> _showTracks;
    private readonly Action<string> _setStatus;

    public PlaylistScreen(
        MusicApplication music,
        IPlaylistRepository playlists,
        Func<string, IReadOnlyList<Track>, Task> showTracks,
        Action<string> setStatus)
    {
        _music = music;
        _playlists = playlists;
        _showTracks = showTracks;
        _setStatus = setStatus;
    }

    public async Task ShowAsync()
    {
        var selected = 0;
        var playlists = await _playlists.GetAllAsync();
        var renderedWidth = -1;
        var renderedHeight = -1;
        var selectionDirty = false;
        while (true)
        {
            selected = playlists.Count == 0 ? 0 : Math.Clamp(selected, 0, playlists.Count - 1);
            if (selectionDirty && renderedWidth >= 0)
            {
                DrawLibraryRows(playlists, selected);
                selectionDirty = false;
            }
            var currentSelection = selected;
            var key = TerminalInput.ReadResponsive(
                () => DrawLibrary(playlists, currentSelection),
                ref renderedWidth,
                ref renderedHeight);

            switch (key.Key)
            {
                case ConsoleKey.UpArrow when playlists.Count > 0:
                    selected = selected == 0 ? playlists.Count - 1 : selected - 1;
                    selectionDirty = true;
                    break;
                case ConsoleKey.DownArrow when playlists.Count > 0:
                    selected = (selected + 1) % playlists.Count;
                    selectionDirty = true;
                    break;
                case ConsoleKey.Enter when playlists.Count > 0:
                    await _showTracks($"PLAYLIST: {playlists[selected].Name}", playlists[selected].Tracks);
                    renderedWidth = -1;
                    break;
                case ConsoleKey.I:
                    await ImportYouTubePlaylistAsync();
                    playlists = await _playlists.GetAllAsync();
                    renderedWidth = -1;
                    break;
                case ConsoleKey.N:
                    await CreatePlaylistAsync();
                    playlists = await _playlists.GetAllAsync();
                    renderedWidth = -1;
                    break;
                case ConsoleKey.Delete when playlists.Count > 0:
                case ConsoleKey.Backspace when playlists.Count > 0:
                    await _playlists.DeleteAsync(playlists[selected].Id);
                    _setStatus($"Deleted playlist: {playlists[selected].Name}");
                    playlists = await _playlists.GetAllAsync();
                    renderedWidth = -1;
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    public async Task AddTrackAsync(Track track)
    {
        var playlists = (await _playlists.GetAllAsync()).ToList();
        if (playlists.Count == 0)
        {
            var created = await CreatePlaylistAsync();
            if (created is null)
                return;
            playlists.Add(created);
        }

        var selected = 0;
        var width = -1;
        var height = -1;
        var selectionDirty = false;
        while (true)
        {
            if (selectionDirty && width >= 0)
            {
                DrawPickerRows(playlists, selected);
                selectionDirty = false;
            }
            var current = selected;
            var key = TerminalInput.ReadResponsive(
                () => DrawPlaylistPicker(playlists, current, track),
                ref width,
                ref height);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selected = selected == 0 ? playlists.Count - 1 : selected - 1;
                    selectionDirty = true;
                    break;
                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % playlists.Count;
                    selectionDirty = true;
                    break;
                case ConsoleKey.Enter:
                    var playlist = playlists[selected];
                    if (playlist.Tracks.Any(item => item.Id == track.Id))
                    {
                        _setStatus($"Already in playlist: {playlist.Name}");
                        return;
                    }
                    await _playlists.SaveAsync(playlist with
                    {
                        Tracks = playlist.Tracks.Append(track).ToList()
                    });
                    _setStatus($"Added {track.Title} to {playlist.Name}");
                    return;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    private async Task ImportYouTubePlaylistAsync()
    {
        var url = ReadText(
            " YOUTUBE PLAYLIST ",
            "Paste a public or unlisted playlist URL. Escape or empty Enter goes back.",
            "URL: ");
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            DrawMessage("Loading playlist tracks...", "Large playlists may take a moment.");
            var result = await _music.GetPlaylistAsync(url);
            if (result.Tracks.Count == 0)
            {
                Notice("The playlist contains no available videos.");
                return;
            }

            var saved = (await _playlists.GetAllAsync())
                .FirstOrDefault(item => string.Equals(item.SourceUrl, url, StringComparison.OrdinalIgnoreCase));
            var playlist = new SavedPlaylist(
                saved?.Id ?? Guid.NewGuid().ToString("N"),
                result.Title,
                url,
                result.Tracks);
            await _playlists.SaveAsync(playlist);
            _setStatus($"Saved playlist: {playlist.Name}");
            await _showTracks($"PLAYLIST: {playlist.Name}", playlist.Tracks);
        }
        catch (Exception exception)
        {
            _setStatus($"Playlist failed: {exception.Message}");
            Notice($"Could not load playlist: {exception.Message}");
        }
    }

    private async Task<SavedPlaylist?> CreatePlaylistAsync()
    {
        var name = ReadText(
            " NEW PLAYLIST ",
            "Enter a playlist name. Escape or empty Enter goes back.",
            "Name: ");
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var playlist = new SavedPlaylist(Guid.NewGuid().ToString("N"), name, null, []);
        await _playlists.SaveAsync(playlist);
        _setStatus($"Created playlist: {name}");
        return playlist;
    }

    private static string? ReadText(string title, string instruction, string label)
    {
        var input = new StringBuilder();
        var lastWidth = -1;
        var lastHeight = -1;
        Console.CursorVisible = true;
        try
        {
            while (true)
            {
                if (lastWidth != Console.WindowWidth || lastHeight != Console.WindowHeight)
                {
                    (lastWidth, lastHeight) = TerminalInput.WaitForStableWindowSize();
                    TerminalCanvas.Clear();
                    TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 7, title);
                    TerminalCanvas.WriteAt(2, 2, TerminalCanvas.Fit(instruction, TerminalCanvas.Width - 4), ConsoleColor.DarkGray);
                }

                var available = Math.Max(1, TerminalCanvas.Width - label.Length - 4);
                TerminalCanvas.WriteAt(2, 4, label, ConsoleColor.Cyan);
                TerminalCanvas.WriteAt(2 + label.Length, 4,
                    TerminalCanvas.Fit(input.ToString(), available).PadRight(available), ConsoleColor.White);
                Console.SetCursorPosition(
                    Math.Min(Console.WindowWidth - 1, 2 + label.Length + Math.Min(input.Length, available - 1)), 4);

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape)
                    return null;
                if (key.Key == ConsoleKey.Enter)
                    return input.ToString().Trim();
                if (key.Key == ConsoleKey.Q && input.Length == 0)
                    return null;
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                    input.Length--;
                else if (!char.IsControl(key.KeyChar))
                    input.Append(key.KeyChar);
            }
        }
        finally { Console.CursorVisible = false; }
    }

    private static void DrawLibrary(IReadOnlyList<SavedPlaylist> playlists, int selected)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, TerminalCanvas.Height, " PLAYLISTS ");
        TerminalCanvas.WriteAt(2, 2,
            "I: Import YouTube   N: New   Enter: Open   Delete: Remove   Q: Home",
            ConsoleColor.DarkGray);
        if (playlists.Count == 0)
        {
            TerminalCanvas.WriteAt(2, 4, "No saved playlists. Press I to import or N to create one.", ConsoleColor.Yellow);
            return;
        }
        DrawLibraryRows(playlists, selected);
    }

    private static void DrawLibraryRows(IReadOnlyList<SavedPlaylist> playlists, int selected)
    {
        var rows = Math.Max(1, TerminalCanvas.Height - 6);
        var first = Math.Clamp(selected - rows / 2, 0, Math.Max(0, playlists.Count - rows));
        var last = Math.Min(playlists.Count, first + rows);
        for (var index = first; index < last; index++)
        {
            var active = index == selected;
            var text = $"{(active ? ">" : " ")} {playlists[index].Name} ({playlists[index].Tracks.Count} tracks)";
            TerminalCanvas.WriteAt(2, 4 + index - first,
                TerminalCanvas.Fit(text, TerminalCanvas.Width - 4).PadRight(Math.Max(1, TerminalCanvas.Width - 4)),
                active ? ConsoleColor.Black : ConsoleColor.Gray,
                active ? ConsoleColor.Cyan : ConsoleColor.Black);
        }
        for (var row = last - first; row < rows; row++)
            TerminalCanvas.WriteAt(2, 4 + row, new string(' ', Math.Max(1, TerminalCanvas.Width - 4)), ConsoleColor.Gray);
    }

    private static void DrawPlaylistPicker(
        IReadOnlyList<SavedPlaylist> playlists,
        int selected,
        Track track)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, TerminalCanvas.Height, " ADD TO PLAYLIST ");
        TerminalCanvas.WriteAt(2, 2, TerminalCanvas.Fit(track.Title, TerminalCanvas.Width - 4), ConsoleColor.Cyan);
        TerminalCanvas.WriteAt(2, 3, "Choose a playlist and press Enter. Q: Cancel", ConsoleColor.DarkGray);
        DrawPickerRows(playlists, selected);
    }

    private static void DrawPickerRows(IReadOnlyList<SavedPlaylist> playlists, int selected)
    {
        var rows = Math.Max(1, TerminalCanvas.Height - 6);
        var first = Math.Clamp(selected - rows / 2, 0, Math.Max(0, playlists.Count - rows));
        var last = Math.Min(playlists.Count, first + rows);
        for (var index = first; index < last; index++)
        {
            var active = index == selected;
            TerminalCanvas.WriteAt(2, 5 + index - first,
                TerminalCanvas.Fit($"{(active ? ">" : " ")} {playlists[index].Name}", TerminalCanvas.Width - 4)
                    .PadRight(Math.Max(1, TerminalCanvas.Width - 4)),
                active ? ConsoleColor.Black : ConsoleColor.Gray,
                active ? ConsoleColor.Cyan : ConsoleColor.Black);
        }
    }

    private static void DrawMessage(string first, string second)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 6, " YOUTUBE PLAYLIST ");
        TerminalCanvas.WriteAt(2, 2, first, ConsoleColor.Cyan);
        TerminalCanvas.WriteAt(2, 3, second, ConsoleColor.DarkGray);
    }

    private static void Notice(string message)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 6, " NOTICE ");
        TerminalCanvas.WriteAt(2, 2, TerminalCanvas.Fit(message, TerminalCanvas.Width - 4), ConsoleColor.Yellow);
        TerminalCanvas.WriteAt(2, 4, "Press any key to continue", ConsoleColor.DarkGray);
        Console.ReadKey(intercept: true);
    }
}
