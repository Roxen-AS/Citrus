# Subtitle and archive engines

Register `new SubtitleEngine()` and `new ArchiveEngine()` with the shared
operation catalog/engine registry. Both implement `IConversionEngine` and run
outside the calling UI thread. The job queue controls concurrency.

- `subtitle.vtt`, `subtitle.srt`, `subtitle.txt` convert one SRT or WebVTT input.
  Ordinary batches should dispatch one request per file. Output is UTF-8 without
  BOM and uses LF. BOM/CRLF inputs, multiline text, hours above 24, comma/dot
  milliseconds, and WebVTT short timestamps are supported. Malformed timestamps
  report a line number. VTT identifiers/settings survive VTT output; SRT gets
  sequential indices and omits VTT-only settings. VTT output preserves REGION
  definitions before cues; SRT/TXT omit them. Every nonempty VTT payload line,
  including whitespace-only lines, is preserved. NOTE/STYLE blocks and header
  metadata are omitted. TXT retains cue text (including
  any inline markup) with a blank line between cues.
- `archive.zip` and `archive.tar` are explicit group conversions of selected regular
  files, stored by basename in selection order. Duplicate basenames are rejected.
  Directory selection and recursive folder creation are not supported. Inputs from
  different directories require an explicit destination.
- `archive.gzip` compresses one file, retaining its basename (`file.txt.gz`).
- `archive.extract` is a Tools operation taking one `.zip`, `.tar`, `.gz`, `.gzip`
  or `.rar` input. It returns a directory named `<archive stem>-extracted`, adding
  a suffix when that destination exists. GZIP produces a single file named after
  the archive without its final extension; embedded GZIP names are ignored. A
  `.tar.gz` becomes a TAR file and requires a second extraction operation.

Every operation writes a copy through `OutputWriter`. Extraction uses a fresh
staging directory and commits it atomically after all entries succeed. A rejected
entry, decompression failure, or cancellation removes the staging directory.
Paths use Windows-compatible validation on every OS, rejecting absolute/drive/UNC
paths, traversal, alternate streams, device names, trailing dots/spaces,
case-insensitive duplicates, file/directory conflicts, and links/reparse entries.
Conventional TAR `./` root directories and leading `./` prefixes are supported.
Only a directory may refer to the extraction root, and it still counts as an
entry. Prefix aliases such as `./name` and `name` share the same name claim;
duplicate explicit names reject the entire extraction. An explicit directory
after an implicitly created parent is allowed. Interior dot segments remain
unsupported.
ZIP Unix attributes and RAR redirection/Unix metadata are inspected explicitly;
RAR's generic LinkTarget alone does not detect its links. Special TAR entries,
encrypted RAR entries and incomplete RAR entries are rejected. No archive entry
permissions or external link targets are restored.

The fixed release ceilings are 100,000 entries and 20 GiB (21,474,836,480 bytes)
of expanded file data. All returned entries, including directories and empty
files, count toward the entry budget. Actual bytes read from decompressed entry
streams count cumulatively before writing; declared file sizes do not bypass the
budget. The reader may consume one detection byte beyond the budget, but that byte
is never written. Hosts can lower either budget with the `ArchiveEngine`
constructor; they cannot raise the release ceilings. These are expanded payload
limits, not a wall-clock, compressed-input, or parser-memory guarantee. Extraction
cancellation is checked per entry and copy chunk; a synchronous dependency call
may delay observation until it returns.

The extraction root's parent is assumed to be controlled by the current user.
Path checks reject existing reparse ancestors but do not provide an OS-handle
sandbox against another local process racing filesystem mutations.

The Linux tests cover actual engine output and cleanup. They do not establish
Windows Explorer/tray behavior or Windows junction and filesystem semantics.
Windows acceptance must repeat extraction, cancellation, collision and reparse
checks on NTFS. SharpCompress is pinned at 0.50.5 with a managed offline publish
asset and MIT notice; see `packaging/archive-backend.lock.json`.
