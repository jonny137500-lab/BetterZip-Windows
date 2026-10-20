using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArcZipWindows;

/// <summary>
/// Opt-in, loopback-only REST API. It runs only while the desktop app is open and the user starts it.
/// </summary>
public sealed class LocalApiServer : IAsyncDisposable
{
    public const int Port = 47821;
    private readonly ZipArchiveService _archives;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private string? _token;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsRunning => _listener.IsListening;
    public string TokenPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcZipWindows", "api.token");
    public string BaseUrl => $"http://127.0.0.1:{Port}/api/v1/";

    public LocalApiServer(ZipArchiveService archives) => _archives = archives;

    public void Start()
    {
        if (IsRunning) return;
        _token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        File.WriteAllText(TokenPath, _token, new UTF8Encoding(false));
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _stop = new CancellationTokenSource();
        _loop = Task.Run(() => ListenAsync(_stop.Token));
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        _stop?.Cancel();
        _listener.Stop();
        if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch (HttpListenerException) { } catch (ObjectDisposedException) { } }
        _listener.Prefixes.Clear();
        _token = null;
        try { if (File.Exists(TokenPath)) File.Delete(TokenPath); } catch { }
        _stop?.Dispose(); _stop = null; _loop = null;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            // Deliberately process requests serially: archive rewrites cannot race each other and memory stays bounded.
            await HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            if (request.RemoteEndPoint is null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
            { await WriteAsync(context, 403, new { error = "Loopback clients only." }); return; }
            if (request.HttpMethod == "OPTIONS")
            { context.Response.Headers["Allow"] = "GET, POST, OPTIONS"; await WriteAsync(context, 204, new { }); return; }
            if (!Authorized(request)) { await WriteAsync(context, 401, new { error = "Missing or invalid bearer token. Read api.token while the API is enabled." }); return; }

            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (request.HttpMethod == "GET" && path == "/api/v1/health")
            { await WriteAsync(context, 200, new { name = "ArcZip Windows API", version = "v1", status = "ok" }); return; }
            if (request.HttpMethod == "GET" && path == "/api/v1/archive/entries")
            {
                var archive = request.QueryString["path"];
                if (string.IsNullOrWhiteSpace(archive)) { await WriteAsync(context, 400, new { error = "Query parameter 'path' is required." }); return; }
                await WriteAsync(context, 200, _archives.List(archive)); return;
            }
            if (request.HttpMethod == "POST")
            {
                if (request.ContentLength64 < 0 || request.ContentLength64 > 1024 * 1024)
                { await WriteAsync(context, 413, new { error = "JSON request body must be 1 MiB or smaller." }); return; }
                var body = await JsonSerializer.DeserializeAsync<ApiRequest>(request.InputStream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? new ApiRequest();
                switch (path)
                {
                    case "/api/v1/archive/create":
                        Require(body.Destination, "destination");
                        _archives.Create(body.Destination!, body.Files ?? []);
                        await WriteAsync(context, 200, new { ok = true, destination = body.Destination }); return;
                    case "/api/v1/archive/add":
                        Require(body.ArchivePath, "archivePath");
                        _archives.AddFiles(body.ArchivePath!, body.Files ?? []);
                        await WriteAsync(context, 200, new { ok = true }); return;
                    case "/api/v1/archive/delete":
                        Require(body.ArchivePath, "archivePath");
                        _archives.DeleteEntries(body.ArchivePath!, body.Entries ?? []);
                        await WriteAsync(context, 200, new { ok = true }); return;
                    case "/api/v1/archive/rename":
                        Require(body.ArchivePath, "archivePath"); Require(body.OldName, "oldName"); Require(body.NewName, "newName");
                        _archives.RenameEntry(body.ArchivePath!, body.OldName!, body.NewName!);
                        await WriteAsync(context, 200, new { ok = true }); return;
                    case "/api/v1/archive/extract":
                        Require(body.ArchivePath, "archivePath"); Require(body.OutputDirectory, "outputDirectory");
                        _archives.ExtractEntries(body.ArchivePath!, body.Entries ?? [], body.OutputDirectory!);
                        await WriteAsync(context, 200, new { ok = true }); return;
                }
            }
            await WriteAsync(context, 404, new { error = "Unknown endpoint." });
        }
        catch (JsonException ex) { await SafeWriteAsync(context, 400, new { error = "Invalid JSON", detail = ex.Message }); }
        catch (ArgumentException ex) { await SafeWriteAsync(context, 400, new { error = ex.Message }); }
        catch (FileNotFoundException ex) { await SafeWriteAsync(context, 404, new { error = ex.Message }); }
        catch (Exception ex) { await SafeWriteAsync(context, 500, new { error = ex.Message }); }
        finally { try { context.Response.Close(); } catch { } }
    }

    private bool Authorized(HttpListenerRequest request)
    {
        if (_token is null) return false;
        var supplied = request.Headers["Authorization"];
        if (supplied is null || !supplied.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var actual = Encoding.UTF8.GetBytes(supplied[7..]);
        var expected = Encoding.UTF8.GetBytes(_token);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static void Require(string? value, string name)
    { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"'{name}' is required."); }

    private static async Task WriteAsync(HttpListenerContext context, int status, object value)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentEncoding = Encoding.UTF8;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        context.Response.ContentLength64 = bytes.Length;
        if (status != 204) await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }
    private static async Task SafeWriteAsync(HttpListenerContext context, int status, object value)
    { try { await WriteAsync(context, status, value).ConfigureAwait(false); } catch { } }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed class ApiRequest
    {
        public string? ArchivePath { get; set; }
        public string? Destination { get; set; }
        public string? OutputDirectory { get; set; }
        public string? OldName { get; set; }
        public string? NewName { get; set; }
        public string[]? Files { get; set; }
        public string[]? Entries { get; set; }
    }
}
