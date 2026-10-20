using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArcZipWindows;

/// <summary>
/// Opt-in loopback-only REST API. Uses TcpListener to avoid HTTP.sys URL ACL/admin requirements.
/// </summary>
public sealed class LocalApiServer : IAsyncDisposable
{
    public const int Port = 47821;
    private readonly ZipArchiveService _archives;
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private string? _token;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;

    public bool IsRunning => _listener is not null;
    public string TokenPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcZipWindows", "api.token");
    public string BaseUrl => $"http://127.0.0.1:{Port}/api/v1/";

    public LocalApiServer(ZipArchiveService archives) => _archives = archives;

    public void Start()
    {
        if (IsRunning) return;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        File.WriteAllText(TokenPath, token, new UTF8Encoding(false));
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start(16);
            _listener = listener;
            _token = token;
            _stop = new CancellationTokenSource();
            _loop = Task.Run(() => ListenAsync(listener, _stop.Token));
        }
        catch
        {
            try { File.Delete(TokenPath); } catch { }
            _token = null;
            _listener = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        if (listener is null) return;
        _stop?.Cancel();
        listener.Stop();
        _listener = null;
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }
        _token = null;
        try { if (File.Exists(TokenPath)) File.Delete(TokenPath); } catch { }
        _stop?.Dispose();
        _stop = null;
        _loop = null;
    }

    private async Task ListenAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            using (client)
            {
                // Serial requests keep memory bounded and prevent simultaneous archive rewrites from racing.
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
            if (request is null) return;
            if (!Authorized(request.Headers))
            { await WriteResponseAsync(stream, 401, new { error = "Missing or invalid bearer token. Read api.token while the API is enabled." }); return; }

            var uri = new Uri(new Uri("http://127.0.0.1/"), request.Target);
            var path = uri.AbsolutePath.TrimEnd('/');
            if (request.Method == "GET" && path == "/api/v1/health")
            { await WriteResponseAsync(stream, 200, new { name = "ArcZip Windows API", version = "v1", status = "ok" }); return; }
            if (request.Method == "POST" && path == "/api/v1/archive/verify")
            {
                if (request.Body.Length == 0) { await WriteResponseAsync(stream, 400, new { error = "A JSON request body is required." }); return; }
                var verifyBody = JsonSerializer.Deserialize<ApiRequest>(request.Body, JsonOptions) ?? new ApiRequest();
                Require(verifyBody.ArchivePath, "archivePath");
                var verifiedEntries = _archives.VerifyAny(verifyBody.ArchivePath!, verifyBody.Password);
                await WriteResponseAsync(stream, 200, new { ok = true, verifiedEntries }); return;
            }
            if (request.Method == "GET" && path == "/api/v1/archive/entries")
            {
                var archive = QueryValue(uri.Query, "path");
                if (string.IsNullOrWhiteSpace(archive)) { await WriteResponseAsync(stream, 400, new { error = "Query parameter 'path' is required." }); return; }
                await WriteResponseAsync(stream, 200, _archives.ListAny(archive)); return;
            }
            if (request.Method == "POST" && path == "/api/v1/archive/list")
            {
                if (request.Body.Length == 0) { await WriteResponseAsync(stream, 400, new { error = "A JSON request body is required." }); return; }
                var listBody = JsonSerializer.Deserialize<ApiRequest>(request.Body, JsonOptions) ?? new ApiRequest();
                Require(listBody.ArchivePath, "archivePath");
                await WriteResponseAsync(stream, 200, _archives.ListAny(listBody.ArchivePath!, listBody.Password)); return;
            }
            if (request.Method == "POST")
            {
                if (request.Body.Length == 0) { await WriteResponseAsync(stream, 400, new { error = "A JSON request body is required." }); return; }
                var body = JsonSerializer.Deserialize<ApiRequest>(request.Body, JsonOptions) ?? new ApiRequest();
                switch (path)
                {
                    case "/api/v1/archive/create":
                        Require(body.Destination, "destination");
                        _archives.CreateFromPathsAny(body.Destination!, body.Files ?? [], body.Overwrite, body.CompressionLevel ?? 6);
                        await WriteResponseAsync(stream, 200, new { ok = true, destination = body.Destination }); return;
                    case "/api/v1/archive/add":
                        Require(body.ArchivePath, "archivePath");
                        _archives.AddPaths(body.ArchivePath!, body.Files ?? []);
                        await WriteResponseAsync(stream, 200, new { ok = true }); return;
                    case "/api/v1/archive/delete":
                        Require(body.ArchivePath, "archivePath");
                        _archives.DeleteEntries(body.ArchivePath!, body.Entries ?? []);
                        await WriteResponseAsync(stream, 200, new { ok = true }); return;
                    case "/api/v1/archive/rename":
                        Require(body.ArchivePath, "archivePath"); Require(body.OldName, "oldName"); Require(body.NewName, "newName");
                        _archives.RenameEntry(body.ArchivePath!, body.OldName!, body.NewName!);
                        await WriteResponseAsync(stream, 200, new { ok = true }); return;
                    case "/api/v1/archive/replace":
                        Require(body.ArchivePath, "archivePath"); Require(body.EntryName, "entryName"); Require(body.FilePath, "filePath");
                        _archives.ReplaceEntry(body.ArchivePath!, body.EntryName!, body.FilePath!);
                        await WriteResponseAsync(stream, 200, new { ok = true }); return;
                    case "/api/v1/archive/extract":
                        Require(body.ArchivePath, "archivePath"); Require(body.OutputDirectory, "outputDirectory");
                        _archives.ExtractEntriesAny(body.ArchivePath!, body.Entries ?? [], body.OutputDirectory!, body.Password);
                        await WriteResponseAsync(stream, 200, new { ok = true }); return;
                }
            }
            await WriteResponseAsync(stream, 404, new { error = "Unknown endpoint." });
        }
        catch (OperationCanceledException) { }
        catch (InvalidDataException ex) { await SafeWriteAsync(stream, 400, new { error = ex.Message }); }
        catch (JsonException ex) { await SafeWriteAsync(stream, 400, new { error = "Invalid JSON", detail = ex.Message }); }
        catch (ArgumentException ex) { await SafeWriteAsync(stream, 400, new { error = ex.Message }); }
        catch (FileNotFoundException ex) { await SafeWriteAsync(stream, 404, new { error = ex.Message }); }
        catch (Exception ex) { await SafeWriteAsync(stream, 500, new { error = ex.Message }); }
    }

    private bool Authorized(IReadOnlyDictionary<string, string> headers)
    {
        if (_token is null || !headers.TryGetValue("authorization", out var supplied) ||
            !supplied.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var actual = Encoding.UTF8.GetBytes(supplied[7..].Trim());
        var expected = Encoding.UTF8.GetBytes(_token);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static void Require(string? value, string name)
    { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"'{name}' is required."); }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0].Replace('+', ' ')).Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString((parts.Length > 1 ? parts[1] : "").Replace('+', ' '));
        }
        return null;
    }

    private static async Task<RequestData?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var headerBuffer = new MemoryStream();
        var one = new byte[1];
        var matched = 0;
        while (headerBuffer.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            headerBuffer.WriteByte(one[0]);
            matched = (matched, one[0]) switch
            {
                (0, 13) => 1, (1, 10) => 2, (2, 13) => 3, (3, 10) => 4,
                (_, 13) => 1, _ => 0
            };
            if (matched == 4) break;
        }
        if (matched != 4) throw new InvalidDataException("HTTP headers are too large or malformed.");
        var headerText = Encoding.ASCII.GetString(headerBuffer.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var first = lines[0].Split(' ', 3);
        if (first.Length < 2) throw new InvalidDataException("Malformed HTTP request line.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrEmpty(line)) break;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }
        byte[] body;
        if (headers.TryGetValue("transfer-encoding", out var transferEncoding))
        {
            if (!transferEncoding.Equals("chunked", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsupported Transfer-Encoding.");
            body = await ReadChunkedBodyAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var length = 0;
            if (headers.TryGetValue("content-length", out var contentLength) &&
                (!int.TryParse(contentLength, out length) || length < 0 || length > MaxBodyBytes))
                throw new InvalidDataException("JSON request body must be 1 MiB or smaller.");
            body = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await stream.ReadAsync(body.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new InvalidDataException("Request body ended unexpectedly.");
                offset += read;
            }
        }
        return new RequestData(first[0].ToUpperInvariant(), first[1], headers, body);
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var line = await ReadAsciiLineAsync(stream, cancellationToken).ConfigureAwait(false);
            var semicolon = line.IndexOf(';');
            var sizeText = (semicolon >= 0 ? line[..semicolon] : line).Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var chunkSize) || chunkSize < 0)
                throw new InvalidDataException("Malformed chunked request body.");
            if (chunkSize == 0)
            {
                // Consume optional trailers through the terminating blank line.
                while ((await ReadAsciiLineAsync(stream, cancellationToken).ConfigureAwait(false)).Length > 0) { }
                return body.ToArray();
            }
            if (body.Length + chunkSize > MaxBodyBytes)
                throw new InvalidDataException("JSON request body must be 1 MiB or smaller.");
            var chunk = new byte[chunkSize];
            var offset = 0;
            while (offset < chunkSize)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(offset, chunkSize - offset), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new InvalidDataException("Chunked request body ended unexpectedly.");
                offset += read;
            }
            body.Write(chunk, 0, chunk.Length);
            var ending = new byte[2];
            var endingRead = 0;
            while (endingRead < 2)
            {
                var read = await stream.ReadAsync(ending.AsMemory(endingRead, 2 - endingRead), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new InvalidDataException("Malformed chunk terminator.");
                endingRead += read;
            }
            if (ending[0] != 13 || ending[1] != 10) throw new InvalidDataException("Malformed chunk terminator.");
        }
    }

    private static async Task<string> ReadAsciiLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var one = new byte[1];
        while (line.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new InvalidDataException("Unexpected end of chunked request body.");
            line.WriteByte(one[0]);
            var bytes = line.ToArray();
            if (bytes.Length >= 2 && bytes[^2] == 13 && bytes[^1] == 10)
                return Encoding.ASCII.GetString(bytes, 0, bytes.Length - 2);
        }
        throw new InvalidDataException("Chunked request line is too large.");
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", _ => "Internal Server Error" };
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task SafeWriteAsync(NetworkStream stream, int status, object value)
    { try { await WriteResponseAsync(stream, status, value).ConfigureAwait(false); } catch { } }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed record RequestData(string Method, string Target, IReadOnlyDictionary<string, string> Headers, byte[] Body);
    private sealed class ApiRequest
    {
        public string? ArchivePath { get; set; }
        public string? Password { get; set; }
        public string? Destination { get; set; }
        public string? OutputDirectory { get; set; }
        public string? OldName { get; set; }
        public string? NewName { get; set; }
        public string? EntryName { get; set; }
        public string? FilePath { get; set; }
        public string[]? Files { get; set; }
        public string[]? Entries { get; set; }
        public bool Overwrite { get; set; }
        public int? CompressionLevel { get; set; }
    }
}
