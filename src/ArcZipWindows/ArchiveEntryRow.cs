namespace ArcZipWindows;

public sealed class ArchiveEntryRow
{
 public string Name { get; init; } = "";
 public string Kind { get; init; } = "File";
 public long Length { get; init; }
 public string SizeText => Kind == "Folder" ? "—" : FormatSize(Length);
 public DateTimeOffset? Modified { get; init; }
 public string ModifiedText => Modified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
 private static string FormatSize(long bytes) { string[] units = ["B","KB","MB","GB","TB"]; double n=bytes; int i=0; while(n>=1024 && i<units.Length-1){n/=1024;i++;} return $"{n:0.##} {units[i]}"; }
}
