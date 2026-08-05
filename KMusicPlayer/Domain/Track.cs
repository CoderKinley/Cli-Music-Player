namespace KMusicPlayer.Domain;

public sealed record Track(
    string Id,
    string Title,
    string Artist,
    TimeSpan? Duration)
{
    public bool IsLocal => Id.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

    public string DurationText =>
        Duration is null ? "Live" :
        Duration.Value.TotalHours >= 1 ? Duration.Value.ToString(@"h\:mm\:ss") :
        Duration.Value.ToString(@"m\:ss");
}
