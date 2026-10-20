# Local REST API (v1)

The API is **opt-in** and runs only while ArcZip Windows is open and the user has clicked **API: Start**. It listens on loopback only at `http://127.0.0.1:47821/api/v1/`; it is not exposed to the LAN.

When enabled, the bearer token is written to `%LOCALAPPDATA%\ArcZipWindows\api.token`. Send it in every request as `Authorization: Bearer <token>`. Do not share the token or expose the port through a proxy. Any app holding the token can access files the Windows user can access.

## Endpoints

- `GET /health` — health/version response.
- `GET /archive/entries?path=C%3A%5Cpath%5Cfile.zip` — list entries in an unencrypted archive.
- `POST /archive/list` — JSON: `{"archivePath":"C:\\out.zip","password":null}`; provide `password` in the request body for encrypted ZIP/7z/RAR archives.
- `POST /archive/verify` — JSON: `{"archivePath":"C:\\out.zip","password":null}`; reads entries to validate decompression/CRC. Password is optional.
- `POST /archive/create` — JSON: `{"destination":"C:\\out.zip","files":["C:\\in\\a.txt"],"overwrite":false,"compressionLevel":6}`. File and folder paths are accepted; folders are added recursively with relative paths. `overwrite` defaults to false. `compressionLevel` is optional (0–9, default 6).
- `POST /archive/add` — JSON: `{"archivePath":"C:\\out.zip","files":["C:\\in\\b.txt"]}`. Each path can be a file or directory.
- `POST /archive/delete` — JSON: `{"archivePath":"C:\\out.zip","entries":["a.txt"]}`. A folder path ending in `/` deletes its contents recursively.
- `POST /archive/rename` — JSON: `{"archivePath":"C:\\out.zip","oldName":"a.txt","newName":"renamed.txt"}`. Folder renames update descendant paths.
- `POST /archive/replace` — JSON: `{"archivePath":"C:\\out.zip","entryName":"folder/a.txt","filePath":"C:\\temp\\edited.txt"}`.
- `POST /archive/extract` — JSON: `{"archivePath":"C:\\out.zip","entries":["a.txt"],"outputDirectory":"C:\\extract","password":null}`. A folder path ending in `/` extracts its contents recursively. Password is optional.

## Supported write formats

Create/add/delete/rename/replace support ZIP, 7z, TAR and TAR.GZ/TGZ. RAR, ACE/ARJ, and compressed-stream-only formats are read-only. Some multi-volume archives can be read when opened from the first volume; split-volume creation is not supported. Creating password-protected archives is not supported yet.

Requests with JSON bodies are limited to 1 MiB. Archive operations are serialized to avoid concurrent rewrite collisions. Passwords are accepted only in the JSON body, are not written to the token file, and are not intentionally logged by the app.

## Example PowerShell

```powershell
$token = (Get-Content "$env:LOCALAPPDATA\ArcZipWindows\api.token" -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Headers $headers -Uri 'http://127.0.0.1:47821/api/v1/health'
Invoke-RestMethod -Headers $headers -Uri ('http://127.0.0.1:47821/api/v1/archive/entries?path=' + [uri]::EscapeDataString('C:\data\archive.zip'))
```

This is a local desktop integration interface, not a remote/network service. It has not undergone an independent security audit.
