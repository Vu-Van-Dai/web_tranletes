# Testing strategy and results

Results below are from the run on 2026-10-04 (Windows 11 dev box, .NET 10.0.401, Node 22.15,
PostgreSQL 18.1, Chromium 1.63). All suites green.

| Suite | Command | Result |
|---|---|---|
| Backend unit | `dotnet test backend/tests/UnitTests` | **60 passed** |
| Backend integration (real PostgreSQL + libvips, `WebApplicationFactory`) | `dotnet test backend/tests/IntegrationTests` | **68 passed**, 3 skipped (S3 tests need `S3_TEST_ENDPOINT`) |
| R2 client + R2 publishing flow vs S3-compatible server (RustFS in Docker) | `S3_TEST_ENDPOINT=… dotnet test --filter R2` | **3 passed** |
| Cloudflare Worker (vitest, fake R2/cache/fetch) | `npm test` in `cloudflare-worker` | **6 passed**, `tsc` clean |
| Frontend unit (vitest) | `npm test` in `frontend` | **9 passed**; `tsc` clean; ESLint 0 errors (5 fast-refresh warnings) |
| Playwright E2E (real API + production build) | `npx playwright test` | **10 passed** (18 required scenarios + screenshots) |
| Docker Compose smoke (Production mode, HTTPS, R2 via S3 server) | `frontend/scripts/smoke.mjs` | **passed** |
| linux/arm64 image (Oracle A1) under emulation | buildx + smoke | **passed** (libvips aarch64 processed chapters) |
| Load / benchmark | `backend/tests/LoadTests/run-load.ps1` | see [benchmarks](benchmarks/benchmark-local.md) |

## What is covered

**Unit** — CPK1 writer/reader (round trip, raw bytes preserved, header layout, tampering, truncation, bad
magic/version, page limit, overlapping/oversized/unknown entries, golden fixture for TypeScript); SafeZip (zip-slip
names incl. `..`, absolute, drive letters, backslashes; symlinks; junk ignored; natural order; page limit; empty;
decompression bomb ratio; oversized entries/totals; real-byte extraction cap); magic-byte sniffing (exe/svg/gif
rejected); NetVips conversion (PNG/JPEG → WebP, WebP verbatim, downscale, pixel limit, corrupt input); Vietnamese
slugs/search keys; natural sort; chapter state machine; permissions; object-key parser; Argon2id (PHC, rehash,
malformed hashes); JWT (tampered, `alg:none`, wrong key); size parsing; config validation.

**Integration** — auth (register validation in Vietnamese, duplicates, lockout, unknown-user parity, refresh rotation +
reuse detection, logout, foreign Origin, change password, invalid/`alg:none` tokens, registration toggle); **tenant
isolation matrix** (14 endpoint × method combinations with another house's ids → 403 `cross_tenant`, upload into
another house, data untouched + audited, lists never leak, 404 vs 403, forged `X-House-Id`, body house id ignored,
reader/admin/anonymous denied, staff permission matrix, removed members, last owner); publishing (full flow with
package download + SHA-256 + dimensions, unpublish withdraw + republish, new version replaces only on publish,
non-zip and non-multipart uploads, 5 malicious/broken archive kinds, retry, delete rules, comic deletion takedown,
cover pipeline, origin fallback secret + public-only + traversal); upload limits (413, page limit); catalog
(visibility, diacritic-insensitive search, tags, houses, navigation, suspend/activate, Vietnamese A–Z collation,
views → rankings, follows/history); admin (admin-only, forced password change, house management, members, metadata
without pages, audit, stats, system, users disable/enable/reset); security headers, CORS, health, body limits, rate
limiting; **concurrency** (150 concurrent public requests, 10 concurrent uploads with observed image concurrency ≤ 2,
racing publishes → exactly one wins).

**E2E (Playwright, Chromium + Pixel 7 emulation)** — 1 register/logout/login (+ wrong password), 2 admin login,
3 admin creates house + owner account (temporary password), 4 owner forced password change, 5 comic, 6 chapter,
7 upload (multipart with progress), 8 processing status, 9 publish, 10 reader opens comic (+ search without
diacritics), 11 opens chapter, 12 single package download with immutable headers, 13 pages rendered from `blob:` URLs
(decoded, natural order, no per-page network requests), 14 paged mode keyboard + next/previous chapter,
15 mobile (bottom nav, 2-column grid, no horizontal overflow, reader width, workspace drawer), 16 unauthorized
access (UI redirects/forbidden + API 401/403), 17 cross-house access rejected (API + UI), 18 logout. Every page is
watched for console errors, uncaught exceptions and API 5xx ("Google Debug" via the Chrome DevTools Protocol); the
CDP session also verifies the package is served from the browser cache on revisit. Traces, screenshots and video are
kept on failure (`playwright-report/`).

## Benchmark summary (2 logical processors, local store, no CDN)

| Scenario | Errors | p50 | p95 | Notes |
|---|---|---|---|---|
| 10 users login + browse | 0 | 41 ms | 195 ms | 123 req/s |
| 50 users mixed | 0 | 200 ms | 347 ms | 228 req/s, ≤ 27 DB connections |
| 100 concurrent reader metadata | 0 | 168 ms | 205 ms | 607 req/s |
| 100 concurrent 9 MB package downloads from origin | 0 | 1.24 s | 1.27 s | worst case — production serves these from the CDN |
| 50 readers while 4 chapters process | 0 | 104 ms | 122 ms | API stays responsive during processing |
| 10 concurrent 20-page uploads | 0 | — | — | all succeeded in 37 s; max 2 concurrent conversions; ~250 MB RAM |

`IMAGE_PROCESSING_CONCURRENCY=3` was ~15 % faster for the upload burst but raised read p95 to 170 ms and RAM; **2**
is kept as default for a real 2-OCPU host. Kestrel's 1 MB body limit was verified (413) on the real server.
Caveat: `DOTNET_PROCESSOR_COUNT=2` limits .NET's view of the CPU, not the OS scheduler, and the dev box is x64 — re-run
`run-load.ps1` on the A1 (or with `docker run --cpus=2`) for production numbers.

## Local review data

`node frontend/scripts/seed-demo.mjs` (API running on `http://localhost:5080`) fills the **local** dev database with
Vietnamese demo content through the real API: 2 translation houses with logos, 6 comics with covers, chapters in every
state (published, ready-to-publish, draft), an 18+ comic, a staff member with limited permissions and a reader with
follows/history. All demo accounts share the password `TramTruyen-Demo-2026`. The script refuses non-localhost targets
and is idempotent. Never run it against production.
