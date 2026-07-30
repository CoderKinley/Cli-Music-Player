using KMusicPlayer.Application;
using KMusicPlayer.Infrastructure;
using KMusicPlayer.UI;

Console.Title = "Cli Musik";
Console.OutputEncoding = System.Text.Encoding.UTF8;
TerminalWindow.Configure(width: 100, height: 30);

var source = new YouTubeMusicSource();
var favoritesPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "KMusicPlayer",
    "favorites.json");

var favorites = new JsonFavoriteRepository(favoritesPath);
var dataDirectory = Path.GetDirectoryName(favoritesPath)!;
var history = new JsonHistoryRepository(Path.Combine(dataDirectory, "history.json"));
var settings = new JsonSettingsRepository(Path.Combine(dataDirectory, "settings.json"));
var session = new JsonPlaybackSessionRepository(Path.Combine(dataDirectory, "session.json"));
using var player = new MpvAudioPlayer(source);
var app = new MusicApplication(source, favorites, player, history, settings, session);
await app.InitializeAsync();
var terminal = new TerminalApplication(app);

await terminal.RunAsync();
