# Architecture

## Overview

```text
Browser (React SPA, vi-VN, Cloudflare Pages: app.<domain>)
 ├─ api.<domain>/api/*  ─ Cloudflare proxy ─► Oracle A1 (2 OCPU / 12 GB, Docker Compose)
 │                                            ├─ backend: ASP.NET Core (.NET 10) — auth, metadata, admin,
 │                                            │           house workspace, uploads, processing, publishing
 │                                            ├─ postgres (internal Docker network only, no published port)
 │                                            └─ Block Volume /data: comics/, tmp/, logs/, postgres/
 └─ img.<domain>/comics/…/v{n}.pkg ─► Worker ─► caches.default (HIT)
                                        └ MISS ─► R2 ─► (absent) Oracle /internal/objects/{key}
                                                         (shared secret; serves only public objects)
```

Reader traffic never reaches ASP.NET in production: the API returns *metadata* (page sizes, package URL, SHA-256)
and the browser downloads **one immutable package per chapter** from the CDN. Reader 1 in a colo fills the edge
cache from R2; readers 2…n are edge-cache hits.

## Backend: modular monolith (4 projects)

| Project | Responsibility |
|---|---|
| `Domain` | Entities and invariants: `Chapter` state machine, `HousePermissions`, Vietnamese text normalisation, natural sort |
| `Application` | Use cases per module — Identity, Tenancy, Catalog, Publishing, Admin, Auditing, Library, Settings — and ports (`IObjectStore`, `IMediaService`, `IProcessingQueue`, …) |
| `Infrastructure` | EF Core (named query filters, migrations), Argon2id, JWT, storage (block volume, R2, local), NetVips, safe ZIP, CPK1 writer/reader, background services |
| `Api` | Minimal-API endpoint groups, house/admin/permission filters, rate limiting, CORS, security headers, ProblemDetails (Vietnamese), Serilog |

## Chapter lifecycle

```text
Draft ──upload──► Processing ──ok──► Ready ──publish──► Published ──unpublish──► Unpublished ──publish──► Published
                      │                                    │ upload (new version, old stays public)
                      └─fail─► Failed ──retry─► Processing  └─► …Ready vN+1 ──publish──► Published vN+1 (vN superseded)
```

1. **Upload** (`POST …/chapters/{id}/upload`): auth → house context → `UploadChapters` permission → chapter
   ownership → multipart streamed to `/data/tmp/uploads` with a byte cap → ZIP signature → DB rows (package
   v(n+1) `Building`, job `Queued`) → bounded in-memory queue (DB is the source of truth; restarts recover jobs).
2. **Processing** (one job at a time; pages in parallel on `IMAGE_PROCESSING_CONCURRENCY` dedicated threads):
   central-directory validation (zip-slip, symlinks, encryption, entry/size/ratio limits) → natural page order →
   capped extraction to generated names → magic-byte sniffing (JPEG/PNG/static WebP only) → libvips header check
   (≤ 16383 px, ≤ `MAX_IMAGE_PIXELS`) → WebP (valid WebP copied verbatim) → pages on the block volume →
   CPK1 package streamed from the page files with incremental SHA-256 → self-check → `Ready`.
3. **Publish**: under a PostgreSQL advisory lock, upload the single package to R2 (skipped if present), verify size,
   then commit (`Published`, counters). The previous version is withdrawn via the **storage outbox**.
4. **Unpublish / delete**: state changes commit immediately; R2 deletions (and optional Cloudflare purges) run from the
   outbox with retries; the outbox re-checks the package state under the same lock (no publish/withdraw race).

## Resource safety on 2 OCPU

- Image work runs on N dedicated threads (`ImageWorkerPool`), never on the ThreadPool → API latency stays flat
  while chapters process (benchmark: p95 122 ms with 4 chapters processing).
- libvips concurrency = 1 and operation cache disabled → CPU and memory per conversion are bounded.
- **Upload safety net (two previews).** (1) Before uploading, the browser reads only the ZIP central directory
  (`features/house/zipInspect.ts`) and shows the exact page order the server will produce (page n ← original file),
  images in unsupported formats that would be skipped, missing/duplicate page numbers and limit violations; nothing
  is sent until the user confirms. Page order parity with the server's `NaturalStringComparer` is enforced by a shared
  fixture (`frontend/tests/fixtures/natural-order.json`, written by a C# test, asserted by vitest). (2) After processing,
  `/quan-ly-nhom/chuong/{id}/xem-truoc/{v}` downloads the private package of that version (house members only),
  verifies its SHA-256 and shows every page with the archive entry it came from (`chapter_pages.source_name`); the
  chapter can be published from there.
- Argon2id hashing is gated by a semaphore; login/register/change-password are rate-limited per IP (20/min); refresh/logout, which run on every page load, have their own per-IP budget (300/min) because a 256-bit refresh token cannot be brute-forced.
- Other rate limits are keyed by account when the request carries a valid access token and by client IP
  otherwise, so readers sharing one public IP (carrier NAT, schools) do not starve each other: global 600/min,
  public catalog 480/min, workspace/admin/library 300/min, at most 2 concurrent uploads per user.
- Request bodies capped at 1 MB except uploads (`MAX_UPLOAD_SIZE`); request timeouts; slow-body protection.
- One shared `NpgsqlDataSource` (pool max 30) for EF Core, advisory locks and batch upserts.
- No Redis/broker: Cloudflare edge cache + R2 serve the heavy traffic; view counts are aggregated in memory and
  flushed once a minute in one upsert.

## Frontend

- Lazy route groups (public, reader, house workspace, admin) — the admin/workspace code is not shipped to readers.
- Access token in memory only; rotating refresh token in an HttpOnly cookie; refresh is single-flight and
  serialised across tabs with the Web Locks API.
- **Reader engine**: metadata → placeholders at real aspect ratios → streamed package download with progress →
  CPK1 parse + SHA-256 verification → one backing `Blob` → per-page `blob.slice()` → object URLs only inside a
  window (±3 preload, revoke beyond ±6, ≤ 13 alive) → `<img>`. Vertical and paged modes, keyboard (←/→, [ ], Esc),
  tap zones, next/previous chapter, prefetch of the next package near the end (skipped on Save-Data).
- Original "mực & giấy" design system (CSS tokens, light/dark, Be Vietnam Pro), 100 % Vietnamese copy.

## Key decisions

| Decision | Why |
|---|---|
| UUIDv7 ids | Non-enumerable (IDOR defense in depth), index-friendly |
| Versioned, immutable object keys `comics/{comicId}/chapters/{chapterId}/v{n}.pkg` | One-year edge caching; the Worker needs no DB lookup |
| R2 holds only *published* packages | Unpublished content never leaves Oracle; takedowns delete the object |
| Fail-closed named query filters (EF Core 10) | A forgotten tenant check returns nothing instead of another house's data |
| ProblemDetails with stable `code` + Vietnamese `detail` | Vietnamese UI without client-side message tables |
| NetVips (libvips) instead of ImageSharp | Streaming, low memory, fast on ARM; no commercial-licence constraints |
