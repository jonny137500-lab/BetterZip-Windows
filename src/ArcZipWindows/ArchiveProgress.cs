namespace ArcZipWindows;

public sealed record ArchiveProgress(string CurrentEntry, long CompletedBytes, long TotalBytes)
{
    public double PercentComplete => TotalBytes <= 0 ? 0 : Math.Clamp(CompletedBytes * 100d / TotalBytes, 0d, 100d);
}
