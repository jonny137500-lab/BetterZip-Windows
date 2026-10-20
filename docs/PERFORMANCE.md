# Performance and safety principles

- No background service starts automatically. The REST API is opt-in and stops with the application.
- Large file payloads are streamed instead of loading whole files into memory. Entry copies use a bounded 128 KiB buffer.
- Long-running create/add/edit/extract/verify operations are moved off the WPF UI thread so the window can remain responsive.
- Extraction and integrity verification report byte progress and support cancellation.
- The API processes requests serially; per-archive locks reduce collisions between UI/API rewrites.
- ZIP, 7z, TAR and TAR.GZ edits are written to a temporary file and replace the original only after the writer closes successfully.
- Extraction validates normalized output paths before writing entries, including entries from nested archives.
- Multi-format read support uses SharpCompress 0.50.6. Format capabilities differ: ZIP, 7z, TAR and TAR.GZ/TGZ are writable; RAR and reader-only formats are not.
- Text previews are limited to 1 MiB; image previews are limited to 25 MiB and decoded to a bounded width to reduce memory pressure.
- Archive passwords are held only in memory during the open session and are not persisted by the app.

No application can guarantee zero memory use. No numerical performance claim is made until representative benchmarks, load tests, and security tests are run.
