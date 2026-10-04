# Known limitations and recommended next steps

## Known limitations

- **Cloudflare not verified live.** No Cloudflare account/credentials were available: R2, the Worker on a custom
  domain, Pages and the edge cache were verified with an S3-compatible server (RustFS), Worker unit tests with fake
  R2/cache, and the local `/cdn` endpoint that mirrors the Worker. Run the [deployment](deployment.md) checklist
  once against the real account.
- **Benchmarks are indicative.** Measured on an x64 dev box with `DOTNET_PROCESSOR_COUNT=2` (not a hard CPU cap) and
  local disk. The arm64 image was verified functionally under emulation, not for performance.
- **No email.** No address verification or self-service password reset; administrators reset passwords
  (temporary password + forced change).
- **Uploads.** ZIP/CBZ only (no RAR/7z/PDF); static JPEG/PNG/WebP only; images taller/wider than 16 383 px are rejected
  (no automatic slicing of long webtoon strips).
- **Edge copies after takedown.** Unpublishing deletes the R2 object immediately, but copies already in Cloudflare's
  edge cache live until evicted unless `CLOUDFLARE_ZONE_ID`/`CLOUDFLARE_API_TOKEN` are configured for purging.
- **Single API instance.** Rate limits, the processing queue and view counters are in-process (by design for one VM).
  Jobs are durable in PostgreSQL; queued work survives restarts, view counts in the last minute may be lost on crash.
- **SPA without SSR.** Search engines see a client-rendered app (titles are set per page, but no server rendering).
- **Hotlinking.** Package/cover URLs are public and served with `Access-Control-Allow-Origin: *`.
- **Not implemented (out of scope):** comments, error reports per chapter, notifications for followed comics,
  multi-file (non-archive) upload.

## Recommended next steps

1. Provision Cloudflare (R2 bucket + scoped token, Worker custom domain, Pages, cache rule, purge token) and run the
   smoke script against production.
2. Re-run `run-load.ps1` on the A1 instance; tune `IMAGE_PROCESSING_CONCURRENCY`, PostgreSQL memory and the
   Npgsql pool from real numbers.
3. Off-host backups (rclone to a second R2 bucket) and a restore drill; Block Volume snapshots.
4. Observability: ship Serilog JSON to a log store (e.g. Grafana Loki / Better Stack), uptime check on
   `/api/health/ready`, alerts on failed jobs and storage-outbox give-ups.
5. Email provider (verification, password reset, owner invitations instead of temporary passwords).
6. Webtoon support: automatic slicing of very tall images into multiple pages.
7. Optional: prerendering/SSR for comic detail pages (SEO), signed or tokenised package URLs if hotlinking becomes a
   cost problem, Redis only if benchmarks show the API/DB needs shared caching or multi-instance rate limits.
