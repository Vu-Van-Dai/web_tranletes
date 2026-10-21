# Trạm Truyện — multi-translation-house comic platform

Vietnamese comic reading platform where independent **translation houses (nhóm dịch)** publish their work.
Low-cost by design: one Oracle A1 VM (2 OCPU / 12 GB) runs the API + PostgreSQL, while chapter bytes are served
by Cloudflare (edge cache → Worker → R2). Each chapter is delivered as **one immutable, checksummed package**.

| Part | Tech | Folder |
|---|---|---|
| API (modular monolith) | .NET 10, ASP.NET Core minimal APIs, EF Core 10, PostgreSQL, NetVips (libvips) | `backend/` |
| Web app (Vietnamese UI) | React 19, TypeScript, Vite, React Router 7, TanStack Query, Tailwind 4 | `frontend/` |
| Package delivery | Cloudflare Worker + R2 + Cache API | `cloudflare-worker/` |
| Deployment | Docker / Docker Compose (backend + postgres only), runbooks | `deploy/` |
| Documentation | Architecture, schema, API, RBAC, package format, tests, limits | `docs/` |

## Documentation

- [Architecture](docs/architecture.md) · [Database schema](docs/database.md) · [API list](docs/api.md)
- [RBAC & tenant isolation](docs/rbac.md) · [CPK1 package format](docs/package-format.md)
- [Deployment (Oracle A1 + Cloudflare)](docs/deployment.md) · [Testing & results](docs/testing.md)
- [Ads, verified views & revenue](docs/monetization.md) · [Known limitations & next steps](docs/limitations.md) · [UI research](docs/ui-research.md) · [Benchmarks](docs/benchmarks/)

## Local development

Prerequisites: .NET SDK 10, Node 22, PostgreSQL 16+ (with `pg_trgm`), optional Docker.

```bash
cp .env.example .env            # fill DATABASE_CONNECTION_STRING, JWT_SECRET, SEED_ADMIN_*; PACKAGE_STORE=local for dev
# create the database once (UTF-8):  CREATE DATABASE comicplatform_dev ENCODING 'UTF8' TEMPLATE template0;

cd backend && dotnet run --project src/Api            # http://localhost:5080 (migrates + seeds the SystemAdmin)
cd frontend && npm install && npm run dev             # http://localhost:5173 (proxies /api and /cdn)
```

Sign in at `/dang-nhap` with `SEED_ADMIN_USERNAME` / `SEED_ADMIN_PASSWORD` from `.env`, then open **Quản trị hệ thống**
to create a translation house and its owner account.

## Tests

```bash
cd backend && dotnet test ComicPlatform.slnx            # unit + integration (real PostgreSQL, real libvips)
cd frontend && npm run typecheck && npm run lint && npm test && npm run build
cd frontend && npx playwright test                      # 18-scenario E2E flow (starts API + preview build itself)
cd cloudflare-worker && npm test
pwsh backend/tests/LoadTests/run-load.ps1               # load / benchmark report → docs/benchmarks/
```

Integration tests create and drop only databases named `comicplatform_test_*` (E2E: `comicplatform_e2e`, load: `comicplatform_load`).
