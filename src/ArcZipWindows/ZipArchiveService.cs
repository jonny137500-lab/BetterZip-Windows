using System.Collections.Concurrent;
using System.IO.Compression;

namespace ArcZipWindows;

/// <summary>ZIP-only archive operations. Entries stream to disk; complete file payloads are never buffered in memory.</summary>
public sealed class ZipArchiveService
{
    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);
    private static object Gate(string path) => PathLocks.GetOrAdd(Path.GetFullPath(path), _ => new object());

    public IReadOnlyList<ArchiveEntryRow> List(string archivePath)
    {
        lock (Gate(archivePath))
        {
            using var archive = ZipFile.OpenRead(archivePath);
            return archive.Entries.Select(e => new ArchiveEntryRow
            {
                Name = e.FullName,
                Kind = e.FullName.EndsWith('/') ? "Folder" : "File",
                Length = e.Length,
                Modified = e.LastWriteTime
            }).OrderBy(e => e.Kind == "Folder" ? 0 : 1)
              .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void Create(string destination, IEnumerable<string> files, bool overwrite = false)
    {
        var fullPath = Path.GetFullPath(destination);
        lock (Gate(fullPath))
        {
            if (File.Exists(fullPath) && !overwrite)
                throw new IOException("Destination already exists. Set overwrite=true only when replacing it is intended.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        var info = new FileInfo(Path.GetFullPath(file));
                        if (!info.Exists) throw new FileNotFoundException("Input file not found.", file);
                        var entry = archive.CreateEntry(info.Name, CompressionLevel.Optimal);
                        var timestamp = new DateTimeOffset(info.LastWriteTime);
                        if (timestamp.Year >= 1980 && timestamp.Year <= 2107) entry.LastWriteTime = timestamp;
                        using var input = File.OpenRead(info.FullName);
                        using var output = entry.Open();
                        input.CopyTo(output);
                    }
                }
                File.Move(tempPath, fullPath, overwrite);
            }
            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        }
    }

    public void AddFiles(string archivePath, IEnumerable<string> files)
    {
        var additions = files.Select(Path.GetFullPath).ToList();
        lock (Gate(archivePath))
        {
            var names = additions.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Rewrite(archivePath, entry => names.Contains(entry.FullName) ? null : entry.FullName, additions);
        }
    }

    public void DeleteEntries(string archivePath, IEnumerable<string> names)
    {
        var remove = names.ToHashSet(StringComparer.Ordinal);
        if (remove.Count == 0) return;
        lock (Gate(archivePath)) Rewrite(archivePath, entry => remove.Contains(entry.FullName) ? null : entry.FullName, []);
    }

    public void RenameEntry(string archivePath, string oldName, string newName)
    {
        ValidateEntryName(newName);
        lock (Gate(archivePath))
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (!archive.Entries.Any(e => e.FullName == oldName)) throw new FileNotFoundException($"Archive entry not found: {oldName}");
                if (archive.Entries.Any(e => e.FullName == newName && e.FullName != oldName))
                    throw new IOException($"An entry named '{newName}' already exists.");
            }
            Rewrite(archivePath, entry => entry.FullName == oldName ? newName : entry.FullName, []);
        }
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains('\\') ||
            name.Contains(':') || name.Contains('\0'))
            throw new ArgumentException("Enter a safe archive-relative name.");
        var segments = name.TrimEnd('/').Split('/');
        if (segments.Any(p => p.Length == 0 || p == "." || p == ".."))
            throw new ArgumentException("Entry names cannot contain empty, '.' or '..' path segments.");
    }

    private static void Rewrite(string archivePath, Func<ZipArchiveEntry, string?> map, IReadOnlyList<string> additions)
    {
        var fullPath = Path.GetFullPath(archivePath);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var source = ZipFile.OpenRead(fullPath))
            using (var destination = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries)
                {
                    var name = map(entry);
                    if (name is null) continue;
                    var target = destination.CreateEntry(name, CompressionLevel.Optimal);
                    if (entry.LastWriteTime.Year >= 1980 && entry.LastWriteTime.Year <= 2107)
                        target.LastWriteTime = entry.LastWriteTime;
                    using var input = entry.Open();
                    using var output = target.Open();
                    input.CopyTo(output);
                }
                foreach (var file in additions)
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) throw new FileNotFoundException("Input file not found.", file);
                    var target = destination.CreateEntry(info.Name, CompressionLevel.Optimal);
                    var timestamp = new DateTimeOffset(info.LastWriteTime);
                    if (timestamp.Year >= 1980 && timestamp.Year <= 2107) target.LastWriteTime = timestamp;
                    using var input = File.OpenRead(info.FullName);
                    using var output = target.Open();
                    input.CopyTo(output);
                }
            }
            File.Move(tempPath, fullPath, true);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    public void ExtractEntries(string archivePath, IEnumerable<string> names, string outputDirectory)
    {
        var selected = names.ToHashSet(StringComparer.Ordinal);
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        lock (Gate(archivePath))
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries.Where(e => selected.Contains(e.FullName)))
            {
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsafe archive path rejected: {entry.FullName}");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
    }
}
