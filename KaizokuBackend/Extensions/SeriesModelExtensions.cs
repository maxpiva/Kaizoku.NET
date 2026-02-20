namespace KaizokuBackend.Extensions;

public static class SeriesModelExtensions
{
    public static string NormalizeStoragePath(string? path)
    {
        return (path ?? string.Empty).SanitizeDirectory();
    }

    public static int ClampChapterCount(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)value;
    }

    public static int ClampChapterCount(int value)
    {
        return ClampChapterCount((long)value);
    }
}
