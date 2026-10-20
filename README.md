# ArcZip Windows

An independent Windows archive manager inspired by modern archive-workspace workflows. Not affiliated with ArcZip or BetterZip.

## Current status

Early development. The current milestone implements ZIP-only desktop operations and an opt-in local REST API. Full ArcZip feature parity is **not** complete.

### Implemented in source
- Windows WPF desktop UI with search, open, create, add, extract selected/all, rename, delete, drag-and-drop files/folders, and command-line ZIP opening
- ZIP operations stream file contents instead of buffering whole files
- Add folders recursively while retaining relative paths; smoke-tested folder import and extraction in memory
- Export an entry to the default external editor and explicitly save the edited file back into the ZIP
- Local API supports replacing nested entry contents
- Edits rewrite to a temporary archive before replacing the original
- Safer extraction that rejects path traversal
- ZIP entry operations serialized per archive path to reduce concurrent rewrite races
- Optional local REST API, loopback-only, bearer token required, off by default
- GitHub Actions build and ZIP smoke tests

### Not yet implemented / not verified
- CI green status for the latest commit and smoke-test results (check Actions)
- 7z/RAR/ISO/CAB and other formats
- encrypted archives and password management
- split volumes and nested archive browsing
- automatic background monitoring of external editor changes (current workflow requires clicking “Save back to ZIP”)
- Windows Explorer context menu and file associations
- installer, signing, update channel, accessibility review and localization
- benchmarks, load testing, fuzzing and security audit
- guaranteed memory/CPU figures (must be measured; zero memory use is impossible)

## Build on Windows

Install the .NET 8 SDK, then run:

```powershell
dotnet restore src/ArcZipWindows/ArcZipWindows.csproj
dotnet build src/ArcZipWindows/ArcZipWindows.csproj -c Release
dotnet run --project src/ArcZipWindows
```

## Local API

See [docs/API.md](docs/API.md). The API is disabled until the user clicks **API: Start** in the desktop app. It binds only to 127.0.0.1, uses a per-run bearer token stored under `%LOCALAPPDATA%\ArcZipWindows\api.token`, and stops when the app closes. Do not share the token.

## Performance principles

See [docs/PERFORMANCE.md](docs/PERFORMANCE.md). The app does not start a resident service automatically, processes API requests serially, and streams file data. Performance claims require benchmarks on representative archives.

## License

MIT for this project source. Any future third-party archive engine must keep its own license and notices.
