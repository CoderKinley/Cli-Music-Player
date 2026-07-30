using KMusicPlayer.Application;
using KMusicPlayer.Domain;

namespace KMusicPlayer.UI.Screens;

public sealed class SearchScreen
{
    private readonly MusicApplication _music;
    private readonly Func<string, IReadOnlyList<Track>, Task> _showResults;
    private readonly Action<string> _setStatus;

    public SearchScreen(
        MusicApplication music,
        Func<string, IReadOnlyList<Track>, Task> showResults,
        Action<string> setStatus)
    {
        _music = music;
        _showResults = showResults;
        _setStatus = setStatus;
    }

    public async Task ShowAsync()
    {
        while (true)
        {
            var query = SearchInput.Read();
            if (string.IsNullOrWhiteSpace(query))
                return;

            try
            {
                _setStatus($"Searching for: {query}");
                ShowMessage(query, "Searching...");
                var tracks = await _music.SearchAsync(query, 20);
                if (tracks.Count == 0)
                {
                    Notice("No matching tracks were found.");
                    continue;
                }

                await _showResults($"SEARCH: {query}", tracks);
            }
            catch (Exception exception)
            {
                _setStatus($"Search failed: {exception.Message}");
                Notice($"Search failed: {exception.Message}");
            }
        }
    }

    private static void ShowMessage(string query, string message)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 7, " SEARCH ");
        TerminalCanvas.WriteAt(2, 2, $"Search: {query}", ConsoleColor.Cyan);
        TerminalCanvas.WriteAt(2, 4, message, ConsoleColor.DarkGray);
    }

    private static void Notice(string message)
    {
        TerminalCanvas.Clear();
        TerminalCanvas.DrawBox(0, 0, TerminalCanvas.Width, 6, " NOTICE ");
        TerminalCanvas.WriteAt(
            2, 2,
            TerminalCanvas.Fit(message, TerminalCanvas.Width - 4),
            ConsoleColor.Yellow);
        TerminalCanvas.WriteAt(2, 4, "Press any key to continue", ConsoleColor.DarkGray);
        Console.ReadKey(true);
    }
}
