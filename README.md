# ArcZip Windows

An independent Windows archive manager inspired by modern archive workspace workflows. Not affiliated with ArcZip or BetterZip.

## Status

This repository is being bootstrapped. The first milestone is a Windows desktop shell with safe ZIP browsing and editing. Full multi-format parity is not yet implemented.

## Planned stack

- C# / .NET 8 / WPF (first prototype)
- ZIP operations using `System.IO.Compression`
- Windows CI via GitHub Actions

## Initial prototype scope

- Open and browse ZIP archives
- Search archive entries
- Extract selected entries with path traversal protection
- Create ZIP archives from files
- Add, delete and rename ZIP entries via temporary archive rewrite

## Not yet implemented

7z/RAR and other archive backends, encryption, split volumes, nested archive browsing, edit-in-place preview workflows, Explorer integration, packaging/signing, full automated tests and security audit.

## Build

On Windows with the .NET 8 SDK:

```powershell
dotnet restore src/ArcZipWindows/ArcZipWindows.csproj
dotnet build src/ArcZipWindows/ArcZipWindows.csproj -c Release
dotnet run --project src/ArcZipWindows
```

This is an early work in progress; these commands and build are not yet verified in CI.
