namespace KMusicPlayer.UI;

public static class ListNavigation
{
    public static int Page(int selected, int count, int pageSize, int direction) =>
        count == 0 ? 0 : Math.Clamp(selected + Math.Max(1, pageSize) * direction, 0, count - 1);

    public static int FirstVisible(int selected, int count, int pageSize) =>
        Math.Clamp(selected - Math.Max(1, pageSize) / 2, 0, Math.Max(0, count - Math.Max(1, pageSize)));
}
