namespace LumaClip.Models;

public sealed record StorageStats(long DatabaseBytes, long ImagesBytes, long ThumbnailsBytes, int ActiveItems, int DeletedItems)
{
    public long TotalBytes => DatabaseBytes + ImagesBytes + ThumbnailsBytes;
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
