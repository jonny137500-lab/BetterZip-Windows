using System.Net.Http.Json;
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
    Check(service.Verify(archivePath) == 1, "verify ZIP entry data and CRC");
    Check(service.ListAny(archivePath).Any(x => x.Name == "alpha.txt"), "format-aware ZIP listing");

    service.AddFiles(archivePath, [inputB]);
    Check(service.List(archivePath).Any(x => x.Name == "beta.txt"), "add file");

    var folder = Path.Combine(root, "sample-folder");
    Directory.CreateDirectory(Path.Combine(folder, "nested"));
    await File.WriteAllTextAsync(Path.Combine(folder, "nested", "inside.txt"), "nested payload");
    service.AddPaths(archivePath, [folder]);
    Check(service.List(archivePath).Any(x => x.Name == "sample-folder/nested/inside.txt"), "add folder tree with relative paths");
    var extractedFolderFile = Path.Combine(root, "folder-extract");
    service.ExtractEntries(archivePath, ["sample-folder/nested/inside.txt"], extractedFolderFile);
    Check(await File.ReadAllTextAsync(Path.Combine(extractedFolderFile, "sample-folder", "nested", "inside.txt")) == "nested payload", "extract folder tree");

    var sevenZipPath = Path.Combine(root, "sample.7z");
    service.CreateAny(sevenZipPath, [inputA]);
    Check(service.ListAny(sevenZipPath).Any(x => x.Name == "alpha.txt"), "create and list 7z archive");
    Check(service.VerifyAny(sevenZipPath) == 1, "verify 7z contents");
    var sevenZipExtract = Path.Combine(root, "7z-extract");
    service.ExtractEntriesAny(sevenZipPath, ["alpha.txt"], sevenZipExtract);
    Check(await File.ReadAllTextAsync(Path.Combine(sevenZipExtract, "alpha.txt")) == "alpha payload", "extract 7z entry");
    service.AddFiles(sevenZipPath, [inputB]);
    Check(service.ListAny(sevenZipPath).Any(x => x.Name == "beta.txt"), "add file to 7z");
    service.RenameEntry(sevenZipPath, "beta.txt", "renamed-beta.txt");
    Check(service.ListAny(sevenZipPath).Any(x => x.Name == "renamed-beta.txt"), "rename 7z entry");
    service.DeleteEntries(sevenZipPath, ["renamed-beta.txt"]);
    Check(!service.ListAny(sevenZipPath).Any(x => x.Name == "renamed-beta.txt"), "delete 7z entry");

    var tarGzPath = Path.Combine(root, "sample.tar.gz");
    service.CreateAny(tarGzPath, [inputA]);
    Check(service.ListAny(tarGzPath).Any(x => x.Name == "alpha.txt"), "create and list TAR.GZ");
    Check(service.VerifyAny(tarGzPath) == 1, "verify TAR.GZ contents");
    var tarGzExtract = Path.Combine(root, "tar-gz-extract");
    service.ExtractEntriesAny(tarGzPath, ["alpha.txt"], tarGzExtract);
    Check(await File.ReadAllTextAsync(Path.Combine(tarGzExtract, "alpha.txt")) == "alpha payload", "extract TAR.GZ entry");

    var replacement = Path.Combine(root, "alpha.txt");
    await File.WriteAllTextAsync(replacement, "replacement payload");
    service.AddFiles(archivePath, [replacement]);
    Check(service.List(archivePath).Count(x => x.Name == "alpha.txt") == 1, "adding same-name file replaces instead of duplicating entry");
    var replacementExtract = Path.Combine(root, "replacement");
    service.ExtractEntries(archivePath, ["alpha.txt"], replacementExtract);
    Check(await File.ReadAllTextAsync(Path.Combine(replacementExtract, "alpha.txt")) == "replacement payload", "replacement content written");
    var overwriteRejected = false;
    try { service.Create(archivePath, [inputB]); }
    catch (IOException) { overwriteRejected = true; }
    Check(overwriteRejected, "create protects an existing destination by default");

    service.RenameEntry(archivePath, "beta.txt", "renamed.txt");
    Check(service.List(archivePath).Any(x => x.Name == "renamed.txt"), "rename entry");

    var extractPath = Path.Combine(root, "extract");
    service.ExtractEntries(archivePath, ["alpha.txt"], extractPath);
    Check(await File.ReadAllTextAsync(Path.Combine(extractPath, "alpha.txt")) == "replacement payload", "extract selected entry after replacement");

    service.DeleteEntries(archivePath, ["renamed.txt"]);
    Check(!service.List(archivePath).Any(x => x.Name == "renamed.txt"), "delete entry");


    using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
    using (var writer = new StreamWriter(zip.CreateEntry("folder/nested.txt").Open())) writer.Write("before edit");
    var exported = Path.Combine(root, "external-edit.txt");
    service.ExportEntryToFile(archivePath, "folder/nested.txt", exported);
    Check(await File.ReadAllTextAsync(exported) == "before edit", "export entry for external editing");
    await File.WriteAllTextAsync(exported, "after edit");
    service.ReplaceEntry(archivePath, "folder/nested.txt", exported);
    var exportedAgain = Path.Combine(root, "external-edit-verified.txt");
    service.ExportEntryToFile(archivePath, "folder/nested.txt", exportedAgain);
    Check(await File.ReadAllTextAsync(exportedAgain) == "after edit", "replace nested archive entry content");

    var maliciousPath = Path.Combine(root, "malicious.zip");
    using (var zip = ZipFile.Open(maliciousPath, ZipArchiveMode.Create))
    using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("not allowed");
    var rejected = false;
    try { service.ExtractEntries(maliciousPath, ["../escape.txt"], extractPath); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "reject path traversal on extraction");
    Check(!File.Exists(Path.Combine(root, "escape.txt")), "no file written outside extraction directory");


    await using (var api = new LocalApiServer(service))
    {
        api.Start();
        var token = (await File.ReadAllTextAsync(api.TokenPath)).Trim();
        using var client = new HttpClient();
        using var unauthorized = await client.GetAsync(api.BaseUrl + "health");
        Check((int)unauthorized.StatusCode == 401, "API rejects requests without token");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var health = await client.GetFromJsonAsync<Dictionary<string, string>>(api.BaseUrl + "health");
        Check(health is not null && health["status"] == "ok", "local API health endpoint");
        var verifyResponse = await client.PostAsJsonAsync(api.BaseUrl + "archive/verify", new { archivePath });
        Check(verifyResponse.IsSuccessStatusCode, "API ZIP verification endpoint");
        var apiFolder = Path.Combine(root, "api-folder");
        Directory.CreateDirectory(apiFolder);
        await File.WriteAllTextAsync(Path.Combine(apiFolder, "from-api.txt"), "API folder payload");
        var addResponse = await client.PostAsJsonAsync(api.BaseUrl + "archive/add",
            new { archivePath, files = new[] { apiFolder } });
        Check(addResponse.IsSuccessStatusCode, "API accepts directory import");
        Check(service.List(archivePath).Any(x => x.Name == "api-folder/from-api.txt"), "API folder import retains relative path");
        await api.StopAsync();
        Check(!File.Exists(api.TokenPath), "API stop removes token file");
    }

    var rejectedName = false;
    try { service.RenameEntry(archivePath, "alpha.txt", "../escape.txt"); }
    catch (ArgumentException) { rejectedName = true; }
    Check(rejectedName, "reject unsafe rename path");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
