using System.IO;
using System.IO.Compression;
using System.Collections.Concurrent;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Readers;

namespace ArcZipWindows;

/// <summary>ZIP-only archive operations. File payloads are streamed rather than loaded whole into memory.</summary>
public sealed class ZipArchiveService
{
    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);
    private static object Gate(string path) => PathLocks.GetOrAdd(Path.GetFullPath(path), _ => new object());

    private static readonly HashSet<string> ReadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz", ".bz2", ".xz", ".lz", ".zst", ".ar", ".arj", ".ace", ".lzw"
    };

    public static bool CanReadFormat(string path) =>
        ReadableExtensions.Contains(Path.GetExtension(path)) ||
        path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase);

    public static bool CanModifyFormat(string path) =>
        Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".tar", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".tgz", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    private static bool UsesSequentialReader(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".gz", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bz2", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".xz", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".lz", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".zst", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".lzw", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".arj", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ace", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lists supported archive formats without extracting file contents.</summary>
    public IReadOnlyList<ArchiveEntryRow> ListAny(string archivePath, string? password = null)
    {
        if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(password)) return List(archivePath);
        lock (Gate(archivePath))
        {
            var rows = new List<ArchiveEntryRow>();
            if (UsesSequentialReader(archivePath))
            {
                using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    rows.Add(new ArchiveEntryRow
                    {
                        Name = entry.Key ?? string.Empty,
                        Kind = entry.IsDirectory ? "Folder" : "File",
                        Length = entry.Size,
                        CompressedLength = entry.CompressedSize,
                        Modified = entry.LastModifiedTime is DateTime time ? new DateTimeOffset(time) : null
                    });
                }
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                rows.AddRange(archive.Entries.Select(e => new ArchiveEntryRow
                {
                    Name = e.Key ?? string.Empty,
                    DisplayName = e.Key ?? string.Empty,
                    Kind = e.IsDirectory ? "Folder" : "File",
                    Length = e.Size,
                    CompressedLength = e.CompressedSize,
                    Modified = e.LastModifiedTime is DateTime time ? new DateTimeOffset(time) : null
                }));
            }
            return rows.OrderBy(e => e.Kind == "Folder" ? 0 : 1)
              .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private static (ArchiveType Type, WriterOptions Options) GetWriterSettings(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".7z", StringComparison.OrdinalIgnoreCase))
            return (ArchiveType.SevenZip, new SevenZipWriterOptions(CompressionType.LZMA2));
        if (ext.Equals(".tgz", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return (ArchiveType.Tar, WriterOptions.ForTar(CompressionType.GZip));
        if (ext.Equals(".tar", StringComparison.OrdinalIgnoreCase))
            return (ArchiveType.Tar, WriterOptions.ForTar(CompressionType.None));
        throw new NotSupportedException("Writing this format is not supported. Use ZIP, 7z, TAR or TAR.GZ.");
    }


    /// <summary>Creates ZIP, 7z, TAR and TAR.GZ archives.</summary>
    public void CreateAny(string destination, IEnumerable<string> files, bool overwrite = false)
    {
        var fullPath = Path.GetFullPath(destination);
        if (Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Create(fullPath, files, overwrite);
            return;
        }
        var (type, options) = GetWriterSettings(fullPath);
        lock (Gate(fullPath))
        {
            if (File.Exists(fullPath) && !overwrite)
                throw new IOException("Destination already exists. Explicit overwrite confirmation is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(tempPath))
                using (var writer = WriterFactory.OpenWriter(output, type, options))
                {
                    foreach (var file in files)
                    {
                        var info = new FileInfo(Path.GetFullPath(file));
                        if (!info.Exists) throw new FileNotFoundException("Input file not found.", file);
                        using var input = File.OpenRead(info.FullName);
                        writer.Write(info.Name, input, info.LastWriteTime);
                    }
                }
                File.Move(tempPath, fullPath, overwrite);
            }
            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        }
    }

    /// <summary>Reads every non-directory entry to trigger decompression and CRC validation.</summary>
    public int VerifyAny(string archivePath, string? password = null)
    {
        if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(password)) return Verify(archivePath);
        lock (Gate(archivePath))
        {
            var verified = 0;
            if (UsesSequentialReader(archivePath))
            {
                using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory) continue;
                    using var input = reader.OpenEntryStream();
                    input.CopyTo(Stream.Null);
                    verified++;
                }
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    using var input = entry.OpenEntryStream();
                    input.CopyTo(Stream.Null);
                    verified++;
                }
            }
            return verified;
        }
    }

    public int Verify(string archivePath)
    {
        lock (Gate(archivePath))
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var verified = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                using var input = entry.Open();
                input.CopyTo(Stream.Null);
                verified++;
            }
            return verified;
        }
    }

    public IReadOnlyList<ArchiveEntryRow> List(string archivePath)
    {
        lock (Gate(archivePath))
        {
            using var archive = ZipFile.OpenRead(archivePath);
            return archive.Entries.Select(e => new ArchiveEntryRow
            {
                Name = e.FullName,
                DisplayName = e.FullName,
                Kind = e.FullName.EndsWith('/') ? "Folder" : "File",
                Length = e.Length,
                CompressedLength = e.CompressedLength,
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
                        SetTimestamp(entry, info.LastWriteTime);
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
        var additions = files.Select(Path.GetFullPath).Select(file => (Path: file, EntryName: Path.GetFileName(file))).ToList();
        AddEntries(archivePath, additions);
    }

    /// <summary>Add files and recursively add directory contents, retaining each selected directory name.</summary>
    public void AddPaths(string archivePath, IEnumerable<string> paths)
    {
        var additions = new List<(string Path, string EntryName)>();
        foreach (var input in paths.Select(Path.GetFullPath))
        {
            if (File.Exists(input))
            {
                additions.Add((input, Path.GetFileName(input)));
                continue;
            }
            if (!Directory.Exists(input)) throw new FileNotFoundException("Input file or directory not found.", input);
            var directoryName = new DirectoryInfo(input).Name;
            foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(input, file).Replace(Path.DirectorySeparatorChar, '/');
                additions.Add((file, $"{directoryName}/{relative}"));
            }
        }
        AddEntries(archivePath, additions);
    }

    private void AddEntries(string archivePath, List<(string Path, string EntryName)> additions)
    {
        if (additions.Count == 0) return;
        foreach (var item in additions) ValidateEntryName(item.EntryName);
        lock (Gate(archivePath))
        {
            var names = additions.Select(x => x.EntryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            RewriteAny(archivePath, name => names.Contains(name) ? null : name, additions);
        }
    }

    public void DeleteEntries(string archivePath, IEnumerable<string> names)
    {
        var remove = names.ToHashSet(StringComparer.Ordinal);
        if (remove.Count == 0) return;
        lock (Gate(archivePath)) RewriteAny(archivePath, name => remove.Contains(name) ? null : name, []);
    }

    public void RenameEntry(string archivePath, string oldName, string newName)
    {
        ValidateEntryName(newName);
        lock (Gate(archivePath))
        {
            var entries = ListAny(archivePath);
            if (!entries.Any(e => e.Name == oldName)) throw new FileNotFoundException($"Archive entry not found: {oldName}");
            if (entries.Any(e => e.Name == newName && e.Name != oldName))
                throw new IOException($"An entry named '{newName}' already exists.");
            RewriteAny(archivePath, name => name == oldName ? newName : name, []);
        }
    }

    public void ExportEntryToFile(string archivePath, string entryName, string destinationFile, string? password = null)
    {
        lock (Gate(archivePath))
        {
            var fullDestination = Path.GetFullPath(destinationFile);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
            using var output = new FileStream(fullDestination, FileMode.Create, FileAccess.Write, FileShare.None);
            if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(password))
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var entry = archive.GetEntry(entryName) ?? throw new FileNotFoundException($"Archive entry not found: {entryName}");
                if (entry.FullName.EndsWith('/')) throw new ArgumentException("Select a file, not a folder.");
                using var input = entry.Open();
                input.CopyTo(output);
            }
            else if (UsesSequentialReader(archivePath))
            {
                using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                var found = false;
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.Key != entryName || reader.Entry.IsDirectory) continue;
                    using var input = reader.OpenEntryStream();
                    input.CopyTo(output);
                    found = true;
                    break;
                }
                if (!found) throw new FileNotFoundException($"Archive entry not found: {entryName}");
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                var entry = archive.Entries.FirstOrDefault(e => e.Key == entryName) ?? throw new FileNotFoundException($"Archive entry not found: {entryName}");
                if (entry.IsDirectory) throw new ArgumentException("Select a file, not a folder.");
                using var input = entry.OpenEntryStream();
                input.CopyTo(output);
            }
        }
    }

    public void ReplaceEntry(string archivePath, string entryName, string replacementFile)
    {
        ValidateEntryName(entryName);
        var fullReplacement = Path.GetFullPath(replacementFile);
        if (!File.Exists(fullReplacement)) throw new FileNotFoundException("Replacement file not found.", replacementFile);
        lock (Gate(archivePath))
        {
            var existing = ListAny(archivePath).FirstOrDefault(e => e.Name == entryName)
                ?? throw new FileNotFoundException($"Archive entry not found: {entryName}");
            if (existing.Kind == "Folder") throw new ArgumentException("A folder entry cannot be replaced with a file.");
            RewriteAny(archivePath, name => name == entryName ? null : name, [(Path: fullReplacement, EntryName: entryName)]);
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

    private static void SetTimestamp(ZipArchiveEntry entry, DateTime timestamp)
    {
        var offset = new DateTimeOffset(timestamp);
        if (offset.Year >= 1980 && offset.Year <= 2107) entry.LastWriteTime = offset;
    }

    private static void RewriteAny(string archivePath, Func<string, string?> map, IReadOnlyList<(string Path, string EntryName)> additions)
    {
        if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Rewrite(archivePath, entry => map(entry.FullName), additions);
            return;
        }
        if (!CanModifyFormat(archivePath))
            throw new NotSupportedException("This archive format is read-only in ArcZip Windows.");

        var fullPath = Path.GetFullPath(archivePath);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var (type, options) = GetWriterSettings(fullPath);
        try
        {
            using (var output = File.Create(tempPath))
            using (var writer = WriterFactory.OpenWriter(output, type, options))
            {
                if (UsesSequentialReader(fullPath))
                {
                    using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(fullPath);
                    while (reader.MoveToNextEntry())
                    {
                        var entry = reader.Entry;
                        if (entry.IsDirectory) continue;
                        var name = map(entry.Key ?? string.Empty);
                        if (name is null) continue;
                        using var input = reader.OpenEntryStream();
                        writer.Write(name, input, entry.LastModifiedTime);
                    }
                }
                else
                {
                    using var source = ArchiveFactory.OpenArchive(fullPath);
                    foreach (var entry in source.Entries)
                    {
                        if (entry.IsDirectory) continue;
                        var name = map(entry.Key ?? string.Empty);
                        if (name is null) continue;
                        using var input = entry.OpenEntryStream();
                        writer.Write(name, input, entry.LastModifiedTime);
                    }
                }
                foreach (var item in additions)
                {
                    var info = new FileInfo(item.Path);
                    if (!info.Exists) throw new FileNotFoundException("Input file not found.", item.Path);
                    using var input = File.OpenRead(info.FullName);
                    writer.Write(item.EntryName, input, info.LastWriteTime);
                }
            }
            File.Move(tempPath, fullPath, true);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }


    private static void Rewrite(string archivePath, Func<ZipArchiveEntry, string?> map, IReadOnlyList<(string Path, string EntryName)> additions)
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
                    if (entry.LastWriteTime.Year >= 1980 && entry.LastWriteTime.Year <= 2107) target.LastWriteTime = entry.LastWriteTime;
                    using var input = entry.Open();
                    using var output = target.Open();
                    input.CopyTo(output);
                }
                foreach (var item in additions)
                {
                    var info = new FileInfo(item.Path);
                    if (!info.Exists) throw new FileNotFoundException("Input file not found.", item.Path);
                    var target = destination.CreateEntry(item.EntryName, CompressionLevel.Optimal);
                    SetTimestamp(target, info.LastWriteTime);
                    using var input = File.OpenRead(info.FullName);
                    using var output = target.Open();
                    input.CopyTo(output);
                }
            }
            File.Move(tempPath, fullPath, true);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    public void ExtractEntriesAny(string archivePath, IEnumerable<string> names, string outputDirectory, string? password = null)
    {
        if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractEntries(archivePath, names, outputDirectory);
            return;
        }
        var selected = names.ToHashSet(StringComparer.Ordinal);
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        lock (Gate(archivePath))
        {
            if (UsesSequentialReader(archivePath))
            {
                using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    var key = (entry.Key ?? string.Empty).Replace('\\', '/');
                    if (!selected.Contains(key)) continue;
                    var target = GetSafeExtractionPath(root, prefix, key);
                    if (entry.IsDirectory) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = reader.OpenEntryStream();
                    using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    input.CopyTo(output);
                }
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, ReaderOptions.ForFilePath.WithPassword(password));
                foreach (var entry in archive.Entries.Where(e => selected.Contains(e.Key ?? string.Empty)))
                {
                    var key = (entry.Key ?? string.Empty).Replace('\\', '/');
                    var target = GetSafeExtractionPath(root, prefix, key);
                    if (entry.IsDirectory) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = entry.OpenEntryStream();
                    using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    input.CopyTo(output);
                }
            }
        }
    }

    private static string GetSafeExtractionPath(string root, string prefix, string key)
    {
        var target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsafe archive path rejected: {key}");
        return target;
    }

    private static string GetSafeExtractionPath(string root, string prefix, string key)
    {
        var target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsafe archive path rejected: {key}");
        return target;
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
