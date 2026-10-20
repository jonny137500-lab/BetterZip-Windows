using System.IO.Compression;
using ArcZipWindows;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

var root = Path.Combine(Path.GetTempPath(), "ArcZipWindowsTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var inputA = Path.Combine(root, "alpha.txt");
    var inputB = Path.Combine(root, "beta.txt");
    await File.WriteAllTextAsync(inputA, "alpha payload");
    await File.WriteAllTextAsync(inputB, "beta payload");
    var archivePath = Path.Combine(root, "test.zip");
    var service = new ZipArchiveService();

    service.Create(archivePath, [inputA]);
    Check(service.List(archivePath).Any(x => x.Name == "alpha.txt"), "create and list ZIP");

    service.AddFiles(archivePath, [inputB]);
    Check(service.List(archivePath).Any(x => x.Name == "beta.txt"), "add file");

    service.RenameEntry(archivePath, "beta.txt", "renamed.txt");
    Check(service.List(archivePath).Any(x => x.Name == "renamed.txt"), "rename entry");

    var extractPath = Path.Combine(root, "extract");
    service.ExtractEntries(archivePath, ["alpha.txt"], extractPath);
    Check(await File.ReadAllTextAsync(Path.Combine(extractPath, "alpha.txt")) == "alpha payload", "extract selected entry");

    service.DeleteEntries(archivePath, ["renamed.txt"]);
    Check(!service.List(archivePath).Any(x => x.Name == "renamed.txt"), "delete entry");

    var maliciousPath = Path.Combine(root, "malicious.zip");
    using (var zip = ZipFile.Open(maliciousPath, ZipArchiveMode.Create))
    using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("not allowed");
    var rejected = false;
    try { service.ExtractEntries(maliciousPath, ["../escape.txt"], extractPath); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "reject path traversal on extraction");
    Check(!File.Exists(Path.Combine(root, "escape.txt")), "no file written outside extraction directory");

    var rejectedName = false;
    try { service.RenameEntry(archivePath, "alpha.txt", "../escape.txt"); }
    catch (ArgumentException) { rejectedName = true; }
    Check(rejectedName, "reject unsafe rename path");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
