# CPK1 chapter package format

One file per chapter version, built before publishing, never modified afterwards. It contains the **raw WebP bytes**
of every page — no re-encoding, no image stitching — so the browser can slice pages out of a single download.

All integers are little-endian.

```text
Offset 0  Header (32 bytes)
  0  char[4] magic          "CPK1"
  4  u16     formatVersion  1
  6  u16     headerSize     32 (readers must honour larger values)
  8  u32     pageCount      ≥ 1
 12  u32     indexSize      = 24 × pageCount
 16  u64     dataSize       total bytes of the data section
 24  u32     flags          0
 28  u32     reserved       0
Offset headerSize  Index (24 bytes per page)
  0  u32 pageNumber   1..N, contiguous, ascending
  4  u64 offset       relative to the data section start
 12  u32 size         > 0
 16  u16 contentType  1 = image/webp
 18  u16 reserved     0
 20  u16 width        > 0 (WebP max 16383)
 22  u16 height       > 0
Offset headerSize + indexSize  Data: page bytes, contiguous, page order
Offset headerSize + indexSize + dataSize  Trailer: 32-byte SHA-256 of header + index + data
```

Total length = headerSize + indexSize + dataSize + 32. The trailer hash is the package `checksum` stored in
`chapter_packages` and returned by `GET /api/chapters/{id}/reader` as `package.sha256`.

## Validation (identical in C# `PackageReader` and TypeScript `parsePackage`)

magic and version · headerSize ≥ 32 · 1 ≤ pageCount ≤ limit · indexSize = 24 × pageCount · declared sizes match
the actual length · every entry in bounds, contiguous, non-overlapping (no 64-bit overflow) · known content type ·
non-zero dimensions · SHA-256 of everything before the trailer equals the trailer **and** the API checksum.

## Properties

- Built by streaming page files into the output with an incremental hash — the whole chapter is never in RAM.
- Self-checked after writing (header/index re-parsed, trailer compared).
- Cross-language parity: `frontend/tests/fixtures/golden-3pages.pkg` is produced by the C# writer and parsed by
  the TypeScript tests.
- Browser memory: the download goes into one pre-sized buffer, is verified, converted to a single `Blob`, and pages
  are zero-copy `blob.slice()`s turned into object URLs only near the reading position.
