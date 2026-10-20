namespace ArcZipWindows;

public sealed class ArchiveEntryRow
{
 public string Name { get; init; } = "";
 public string DisplayName { get; init; } = "";
 public string Kind { get; init; } = "File";
 public bool IsEncrypted { get; init; }
 public string EncryptionText => IsEncrypted ? "Locked" : "—";
 public long Length { get; init; }
 public long? CompressedLength { get; init; }
 public string CompressedSizeText => Kind == "Folder" || CompressedLength is null ? "—" : FormatSize(CompressedLength.Value);
 public string CompressionRatioText => Kind == "Folder" || CompressedLength is null || Length <= 0 ? "—" : $"{(1d - (double)CompressedLength.Value / Length) * 100:0.#}%";
 public string SizeText => Kind == "Folder" ? "—" : FormatSize(Length);
 public DateTimeOffset? Modified { get; init; }
 public string ModifiedText => Modified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
 private static string FormatSize(long bytes) { string[] units = ["B","KB","MB","GB","TB"]; double n=bytes; int i=0; while(n>=1024 && i<units.Length-1){n/=1024;i++;} return $"{n:0.##} {units[i]}"; }
}
