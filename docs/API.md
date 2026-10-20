# Local REST API (v1)

The API is **opt-in** and runs only while ArcZip Windows is open and the user has clicked **Start API**. It listens on loopback only: `http://127.0.0.1:47821/api/v1/`. It is not exposed to the LAN.

When enabled, the bearer token is written to:

`%LOCALAPPDATA%\ArcZipWindows\api.token`

Send it in every request as `Authorization: Bearer <token>`. Do not share the token or expose the port through a proxy. The API runs with the same Windows permissions as the desktop user; any app holding the token can access files that user can access.

## Endpoints

- `GET /health` — health/version response.
- `GET /archive/entries?path=C%3A%5Cpath%5Cfile.zip` — list supported archive entries.
- `POST /archive/verify` — JSON: `{"archivePath":"C:\\out.zip"}`; streams each file entry to validate decompression/CRC across supported formats.
- `POST /archive/create` — JSON: `{"destination":"C:\\out.zip","files":["C:\\in\\a.txt"],"overwrite":false}`. Existing destination files are protected unless `overwrite` is explicitly `true`.
- `POST /archive/add` — JSON: `{"archivePath":"C:\\out.zip","files":["C:\\in\\b.txt"]}`. Each path may be a file or directory; directories are added recursively under their selected folder name.
- `POST /archive/delete` — JSON: `{"archivePath":"C:\\out.zip","entries":["a.txt"]}`.
- `POST /archive/rename` — JSON: `{"archivePath":"C:\\out.zip","oldName":"a.txt","newName":"renamed.txt"}`.
- `POST /archive/replace` — JSON: `{"archivePath":"C:\\out.zip","entryName":"folder/a.txt","filePath":"C:\\temp\\edited.txt"}`. Replaces the contents of an existing file entry, including nested entries, with a local file.
- `POST /archive/extract` — JSON: `{"archivePath":"C:\\out.zip","entries":["a.txt"],"outputDirectory":"C:\\extract"}`.

Requests with JSON bodies are limited to 1 MiB. Archive operations are serialized to avoid concurrent rewrite collisions. ZIP is the only supported archive format in this milestone. A 200 response means the operation completed; non-2xx responses contain a JSON `error` field.

## Example PowerShell

```powershell
$token = Get-Content "$env:LOCALAPPDATA\ArcZipWindows\api.token" -Raw
$headers = @{ Authorization = "Bearer $($token.Trim())" }
Invoke-RestMethod -Headers $headers -Uri 'http://127.0.0.1:47821/api/v1/health'
Invoke-RestMethod -Headers $headers -Uri ('http://127.0.0.1:47821/api/v1/archive/entries?path=' + [uri]::EscapeDataString('C:\\data\\archive.zip'))
```

This API is a local desktop integration interface, not a remote/network service. It has not undergone a security audit.
