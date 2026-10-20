# Test plan — milestone 1

Current CI performs restore and compilation on Windows. It does not yet execute behavioral tests.

Before calling the ZIP rewrite implementation stable, add automated tests for:
- create/list/extract round-trip
- add duplicate-name behavior
- delete and rename preserving unrelated entries and metadata
- path traversal entries such as `../outside.txt` and absolute paths
- corrupt archive errors and temp-file cleanup
- non-ASCII filenames and large entries
- replacement failure leaving the original archive intact

Status: planned; not yet run.