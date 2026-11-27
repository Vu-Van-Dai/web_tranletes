# One-merge SEO and advertising release (PR #6)

## Scope
- Backend advertises friendly public paths only when the new slug APIs exist. Old UUID links still resolve, then 301 on a full navigation (SPA uses replace navigation).
- Canonical comic: /truyen/{houseSlug}/{comicSlug}; chapter: /doc/{houseSlug}/{comicSlug}/chuong-{number}. Decimal chapters supported. Existing UUID API, package IDs, storage keys, R2 bucket, Worker cache, reader checksums and view tracking stay in place.
- Sitemap proxy requests friendly=true; old backend ignores it until upgraded. Sitemap includes visible comic/house/tag and published non-mature chapter URLs, with stable lastmod. BreadcrumbList is rendered in initial HTML. 50,000 URL cap; partition later if needed.
- Ads provider labels, per-provider kill switches, global direct-script permission and per-script approval; direct scripts now default OFF. Existing ordinary ads remain configured. AdSense opt-in is separate. Use only verified lawful, general-audience creatives; category controls need configuration in the actual provider dashboard. No network automatically registered or activated.
- Manual monthly provider reports are admin-only, stored in existing JSON settings, not inferred from comic views, not used to change existing house revenue calculations. Automatic import requires provider credentials and API integration; no such external integration in this release.
- No EF schema migration in this PR. Future friendly slug rename aliases are not included: avoid changing published slugs until redirect history exists.

## Deployment order
1. Require all GitHub checks green, review PR #6, merge once into main.
2. Confirm the production Cloudflare Pages deployment for that merge succeeds first. Check that existing comic and chapter URLs work and the sitemap is reachable. The old backend still returns UUID links. The Image Worker remains unchanged.
3. On Azure, execute the script below as azureuser. It checks for a fast-forward, preserves the two locally customized deployment files, makes a fresh private DB/config backup, tags the existing image, builds only backend while old API serves, and recreates only backend. It stops if upstream deployment files or migrations changed. It checks readiness and compares package metadata before/after using a published chapter, then probes the actual package host without a purge.

```bash
cd /srv/comicplatform
git fetch origin main
umask 077
release="$(mktemp)"
git show origin/main:deploy/scripts/deploy-seo-release.sh > "$release" &&
bash "$release"
rm -f "$release"
```

Optional: pass a known public chapter UUID to the script. Backup on VPS is not offsite; copy to secure storage separately without exposing .env. Brief API downtime during container recreation is expected. Do not run compose down, reset --hard or docker prune. If a readiness/package check fails the script restores the previous API image, not the Git checkout or database. Do not edit ads config using the old backend after rollback; its older serializer may drop new settings. Verify application logs and frontend manually. Wait for short API/Pages caches to expire, then check old link 301, new link 200, sitemap XML, live URL Inspection, R2/cache headers and all reading flows. Do not purge immutable images. HIT is not guaranteed for oversized objects or a different edge location.

## Validation
CI builds frontend/backend, runs frontend unit tests, public catalog and ad-provider integration tests on isolated PostgreSQL and image Worker cache tests. Production smoke test cannot be replaced by CI.
