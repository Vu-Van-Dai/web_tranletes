# Deployment — Oracle A1 + Cloudflare

## 1. Oracle Cloud (Always Free A1: 2 OCPU, 12 GB)

1. Create an **Ubuntu 24.04 (aarch64)** A1 instance; attach a **Block Volume** and mount it at `/data`
   (`/etc/fstab` with `_netdev,nofail`).
2. Install Docker Engine + Compose plugin. `sudo mkdir -p /data/app /data/postgres && sudo chown 1654 /data/app`
   (1654 = `app` user of the .NET images).
3. Firewall (OCI security list **and** host `ufw`): allow 22 from your IP, allow 443 **only from Cloudflare IP ranges**
   (https://www.cloudflare.com/ips/). Never open 5432 — PostgreSQL has no published port and lives on an
   `internal: true` Docker network.
4. Cloudflare SSL mode **Full (strict)**: create a Cloudflare **Origin CA certificate** for `api.<domain>`, save as
   `deploy/certs/origin.pem` and `deploy/certs/origin.key` (git-ignored). Optionally enable Authenticated Origin Pulls.
5. Create `/srv/comicplatform/.env` from `.env.example` (production values, see below), then:

```bash
git clone <repo> /srv/comicplatform && cd /srv/comicplatform/deploy
export DB_PASSWORD='<strong password>' DATA_ROOT=/data API_HTTPS_URL=';https://+:8443' \
       TLS_CERT_PATH=/certs/origin.pem TLS_KEY_PATH=/certs/origin.key
docker compose --env-file ../.env up -d --build      # builds natively on the A1 (arm64)
curl -s http://127.0.0.1:8080/api/health/ready       # {"status":"ok",...}
```

Images can also be built elsewhere: `docker buildx build --platform linux/arm64 -f deploy/backend.Dockerfile .`
(verified: arm64 image runs and processes chapters under emulation).

**Backups**: `deploy/scripts/backup-postgres.sh` (daily cron) dumps the database with `pg_dump -Fc`; copy dumps off
the VM (e.g. to a separate R2 bucket with `rclone`). Chapter packages can be rebuilt from `/data/app/comics`; snapshot
the Block Volume weekly.

## 2. Cloudflare

| Piece | Setup |
|---|---|
| DNS | `api.<domain>` → A1 public IP (proxied, orange cloud); `app.<domain>` → Pages; `img.<domain>` → Worker custom domain |
| R2 | Create bucket (e.g. `comicplatform-packages`). Create an **R2 API token** scoped to that bucket (Object Read & Write) → `R2_ACCOUNT_ID`, `R2_ACCESS_KEY`, `R2_SECRET_KEY`, `R2_BUCKET`. No public bucket access is needed. |
| Worker | `cd cloudflare-worker && npm ci`; set `bucket_name`, `ORIGIN_URL=https://api.<domain>` and the custom-domain route in `wrangler.toml`; `npx wrangler secret put ORIGIN_SHARED_SECRET` (same value as the API); `npx wrangler deploy`. Must run on a **custom domain** — the Cache API is inert on `*.workers.dev`. |
| Pages | Build command `npm ci && npm run build`, output `dist`, root directory `frontend`. Variables: `PUBLIC_API_URL=https://api.<domain>`, `PUBLIC_APP_NAME`. SPA fallback is automatic; security headers come from `public/_headers`; Pages Functions (`functions/quan-tri`, `functions/quan-ly-nhom`) serve the ad-free `workspace.html` for `/quan-ly-nhom/*` and `/quan-tri/*` (see [monetization](monetization.md)). |
| Cache rule (optional) | For `api.<domain>` paths `/api/comics*`, `/api/chapters/*`, `/api/tags`, `/api/houses*`, `/api/site`, `/api/rankings`: *Eligible for cache, respect origin TTL* → absorbs release-time bursts (origin sends 30–300 s). Never cache `/api/auth/*`, `/api/me/*`, `/api/admin/*`, `/api/translation-house/*`. |
| Purge (optional) | API token with *Zone → Cache Purge* → `CLOUDFLARE_ZONE_ID`, `CLOUDFLARE_API_TOKEN`: unpublished/superseded package URLs are purged from the edge. |

## 3. Environment variables

| Variable | Production value / meaning |
|---|---|
| `DATABASE_CONNECTION_STRING` | set by compose (`Host=postgres;…`) from `DB_PASSWORD`, `DB_USER`, `DB_NAME` |
| `JWT_SECRET` | ≥ 32 random bytes (base64). Startup fails if shorter. |
| `JWT_ISSUER`, `JWT_AUDIENCE`, `ACCESS_TOKEN_MINUTES` (10), `REFRESH_TOKEN_DAYS` (14) | token settings |
| `SEED_ADMIN_EMAIL`, `SEED_ADMIN_USERNAME`, `SEED_ADMIN_PASSWORD` | first SystemAdmin (created once if none exists) |
| `PACKAGE_STORE` | `r2` in production (`local` is refused in Production) |
| `R2_ACCOUNT_ID` or `R2_ENDPOINT`, `R2_ACCESS_KEY`, `R2_SECRET_KEY`, `R2_BUCKET` | R2 S3 API credentials |
| `ORIGIN_SHARED_SECRET` | ≥ 24 chars, shared with the Worker for the origin fallback |
| `CLOUDFLARE_ZONE_ID`, `CLOUDFLARE_API_TOKEN` | optional edge purge |
| `COMIC_STORAGE_PATH`, `TEMP_STORAGE_PATH` | `/data/comics`, `/data/tmp` (set in the image) |
| `MAX_UPLOAD_SIZE` (200MB), `MAX_EXTRACTED_SIZE` (600MB), `MAX_PAGES_PER_CHAPTER` (200) | upload limits |
| `MAX_IMAGE_PIXELS` (40000000), `PAGE_MAX_WIDTH` (2000), `WEBP_QUALITY` (82) | image limits |
| `IMAGE_PROCESSING_CONCURRENCY` (2), `PROCESSING_QUEUE_CAPACITY` (50), `PROCESSING_JOB_TIMEOUT` (00:15:00) | processing |
| `CORS_ALLOWED_ORIGINS` | `https://app.<domain>` (required in Production) |
| `TRUST_CLOUDFLARE_HEADERS` | `true` only when 443 is firewalled to Cloudflare ranges (enables `CF-Connecting-IP`) |
| `PUBLIC_API_URL`, `PUBLIC_IMAGE_URL`, `PUBLIC_APP_NAME` | `https://api.<domain>`, `https://img.<domain>`, site name |

Never commit `.env` or certificates (`.gitignore` covers both). Secrets are read from environment variables only.

## 4. Verified locally

`docker compose` with `ASPNETCORE_ENVIRONMENT=Production`, `PACKAGE_STORE=r2` (pointed at an S3-compatible server),
HTTPS on 8443 with a PEM certificate: health ready, full smoke flow (`frontend/scripts/smoke.mjs`), exactly one
package object in the bucket, PostgreSQL not published, zero error logs. See [testing.md](testing.md).
