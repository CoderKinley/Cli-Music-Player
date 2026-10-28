namespace KMusicPlayer.UI;

public static class ListNavigation
{
    public static int Page(int selected, int count, int pageSize, int direction) =>
        count == 0 ? 0 : Math.Clamp(selected + Math.Max(1, pageSize) * direction, 0, count - 1);

    public static int FirstVisible(int selected, int count, int pageSize) =>
        Math.Clamp(selected - Math.Max(1, pageSize) / 2, 0, Math.Max(0, count - Math.Max(1, pageSize)));

    public static IEnumerable<int> RowsToRedraw(int selected, int? previous, int count, int pageSize)
    {
        var first = FirstVisible(selected, count, pageSize);
        if (previous is { } old && old >= 0 && old < count && FirstVisible(old, count, pageSize) == first)
        {
            yield return old;
            if (selected != old) yield return selected;
        }
        else
            for (var index = first; index < Math.Min(count, first + Math.Max(1, pageSize)); index++)
                yield return index;
    }
}
