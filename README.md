# ArcZip Windows

An independent Windows archive manager built around a modern archive-workspace workflow. Not affiliated with ArcZip or BetterZip.

## Current status

Early development. The core desktop app and local API are implemented, but full commercial-app feature parity is **not** complete. Builds and automated tests are run on Windows through GitHub Actions.

### Implemented
- Windows WPF UI with search, drag-and-drop files/folders, folder navigation, nested archive browsing, and command-line opening of supported archive formats.
- Create archives from files or folders while preserving relative paths.
- Create and modify ZIP, 7z, TAR and TAR.GZ/TGZ archives: add, replace, rename, delete, extract selected/all, and verify integrity.
- Read/list/extract common additional formats supported by SharpCompress, including RAR and compressed TAR formats; RAR/ACE/ARJ and stream-only formats are read-only.
- Read encrypted ZIP/7z/RAR archives with a password prompt or the password-aware local API. Passwords are kept in memory for the current open session and are not persisted by ArcZip.
- Compression profiles (Fast, Balanced, Maximum), with compressed size and space-saved metadata.
- Bounded text and image previews, plus open-in-default-app and external editing.
- Nested archive edits are written back to a writable parent archive when navigating back out.
- Background archive work; extraction and integrity verification report byte progress and can be cancelled.
- Safe extraction path checks, temporary-file rewrite/replace for archive edits, and per-archive operation locking.
- Optional Explorer "Open with ArcZip Windows" integration in the installer. It does not change default file associations unless the user changes them separately.
- Opt-in, loopback-only local REST API with per-run bearer-token authentication. The API is off by default.
- GitHub Actions Windows build, smoke tests, self-contained x64 app artifact, and installer artifact.

### Not implemented or not yet verified
- Creating password-protected archives.
- Creating split-volume archives (some existing multi-volume archives can be read when the first volume is opened).
- ISO/CAB and any format not supported by the archive engine.
- Automatic monitoring of external editor saves; external edits still require an explicit save-back action.
- Code signing, auto-update, full accessibility/localization review, fuzzing, independent security audit, and representative performance benchmarks.
- Guaranteed memory/CPU figures. Streaming avoids loading entire files into memory, but it does not mean zero memory use.

## Build on Windows

Install the .NET 8 SDK, then run:

```powershell
dotnet restore src/ArcZipWindows/ArcZipWindows.csproj
dotnet build src/ArcZipWindows/ArcZipWindows.csproj -c Release
dotnet run --project src/ArcZipWindows
```

## Local API

See [docs/API.md](docs/API.md). The API starts only after the user clicks **API: Start** in the desktop app. It binds to `127.0.0.1:47821`, uses a per-run bearer token stored under `%LOCALAPPDATA%\ArcZipWindows\api.token`, and stops when the app closes. Do not share the token.

## Performance and safety

See [docs/PERFORMANCE.md](docs/PERFORMANCE.md). Large file data is streamed, and archive edits are written to temporary files before replacing the original. Performance claims require benchmarks on representative archives.

## Windows installer

The Windows build workflow publishes self-contained portable builds for `win-x64` (Intel and AMD 64-bit CPUs), `win-x86` (32-bit x86 Windows), and `win-arm64` (64-bit Windows on ARM), plus the `ArcZip-Windows-Setup.exe` installer for x64. Intel and AMD do not need separate binaries: both use the x64 build. ARM32 is not provided because the current .NET WPF app targets x64, x86 and ARM64. The installer is unsigned; Windows SmartScreen may display a warning. Download the matching architecture from this repository's Actions artifacts and review the source before use.

## License

MIT for this project source. SharpCompress is a third-party dependency and retains its own license and notices.
