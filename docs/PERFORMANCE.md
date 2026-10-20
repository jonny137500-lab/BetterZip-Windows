# Performance and memory principles

- No background service starts automatically. The REST API is opt-in and stops with the application.
- Archive entries are listed without extracting their contents.
- API requests are handled serially to cap concurrency and avoid two simultaneous rewrites of one archive.
- ZIP edits rewrite to a temporary file and replace the original only after writing succeeds.
- Large-file streaming uses streams rather than loading whole file contents into memory.

No application can guarantee zero memory use. Performance and memory targets must be measured on representative archives before making numerical claims. Future work: add cancellation/progress, large archive benchmarks, bounded entry listing, and per-archive operation coordination shared by UI and API.
