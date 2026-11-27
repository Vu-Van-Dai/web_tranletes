#!/usr/bin/env bash
# Run only after the merged frontend is deployed and checked. Does not touch R2 or restart postgres.
set -Eeuo pipefail
umask 077
cd /srv/comicplatform
sudo -v
command -v python3 >/dev/null
command -v curl >/dev/null
test "$(git branch --show-current)" = main
test -f .env
git fetch origin main
target="$(git rev-parse origin/main)"
git merge-base --is-ancestor HEAD "$target" || { echo "Not a fast-forward; stop."; exit 1; }
git diff --quiet HEAD "$target" -- deploy/backend.Dockerfile deploy/docker-compose.yml || { echo "Upstream deploy files changed; review local Azure overrides manually."; exit 1; }
if git diff --name-only HEAD "$target" | grep -Ei '(^|/)migrations/' ; then
  echo "Database migrations detected; this no-migration release script cannot deploy them."; exit 1
fi
# Preserve only the two known Azure-specific tracked overrides; refuse other local tracked edits.
unexpected="$(git diff --name-only HEAD | grep -vxE 'deploy/(backend\.Dockerfile|docker-compose\.yml)' || true)"
test -z "$unexpected" || { echo "Unexpected tracked changes; stop."; printf '%s\n' "$unexpected"; exit 1; }
if [ -n "$(git diff --cached --name-only)" ]; then echo "Staged changes; stop."; exit 1; fi
cd deploy
compose=(sudo docker compose --env-file ../.env)
test "$("${compose[@]}" config --services | grep -x backend)" = backend
stamp="$(date +%Y%m%d-%H%M%S)"
backup="$HOME/comicplatform-backups/$stamp"
mkdir -p "$backup"
cp backend.Dockerfile docker-compose.yml "$backup/"
sudo cat ../.env > "$backup/.env"
git diff HEAD > "$backup/local-changes.patch"
git rev-parse HEAD > "$backup/git-commit.txt"
sudo docker exec comicplatform-postgres-1 sh -c 'pg_dump -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}" -Fc' > "$backup/database.dump"
test -s "$backup/database.dump"
sudo docker exec -i comicplatform-postgres-1 pg_restore --list < "$backup/database.dump" > "$backup/database-contents.txt"
# Capture an actual published chapter, optionally supplied as the first argument.
api=http://127.0.0.1:8080/api
curl -fsS --max-time 15 "$api/health/ready" > "$backup/health-before.json"
sample="${1:-}"
if [ -z "$sample" ]; then
  curl -fsS --max-time 15 "$api/comics?pageSize=24" > "$backup/sample-catalog.json"
  sample="$(python3 - "$backup/sample-catalog.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1]))
for comic in data.get("items", []):
    chapters=comic.get("latestChapters") or []
    if not comic.get("isMature") and chapters:
        print(chapters[0]["id"])
        break
PY
)"
fi
if [ -n "$sample" ]; then
  curl -fsS --max-time 15 "$api/chapters/$sample/reader" > "$backup/reader-before.json"
else
  echo "No non-mature sample selected: verify reader and cache manually after deployment."
fi
old_image="$(sudo docker inspect comicplatform-backend-1 --format '{{.Image}}')"
rollback_tag="comicplatform-api:rollback-$stamp"
sudo docker tag "$old_image" "$rollback_tag"
echo "Backup: $backup"
echo "Rollback image: $rollback_tag"
cd ..
git merge --ff-only "$target"
cd deploy
# Keep current containers serving while building the new image.
"${compose[@]}" build backend
rollback() {
  echo "Rolling back API image only to $rollback_tag; preserving DB and checkout."
  sudo docker tag "$rollback_tag" comicplatform-api:latest
  "${compose[@]}" up -d --no-deps --no-build backend
}
if ! "${compose[@]}" up -d --no-deps --no-build backend; then rollback; exit 1; fi
ready=0
for attempt in $(seq 1 30); do
  if curl -fsS --max-time 5 "$api/health/ready" > "$backup/health-after.json"; then ready=1; break; fi
  sleep 2
done
if [ "$ready" -ne 1 ]; then rollback; echo "New backend failed readiness."; exit 1; fi
if [ -n "$sample" ]; then
  if ! curl -fsS --max-time 15 "$api/chapters/$sample/reader" > "$backup/reader-after.json"; then rollback; exit 1; fi
  if ! python3 - "$backup/reader-before.json" "$backup/reader-after.json" <<'PY'
import json, sys
before, after = (json.load(open(p)) for p in sys.argv[1:])
assert before["chapter"]["id"] == after["chapter"]["id"]
assert after["chapter"]["publicPath"].startswith("/doc/")
for key in ("url", "version", "size", "sha256", "format", "pageCount"):
    assert before["package"][key] == after["package"][key], f"Changed package field: {key}"
print("Package URL/version/size/checksum/format/page count unchanged.")
PY
  then
    echo "Package comparison failed (could be a simultaneous republish)."
    rollback
    exit 1
  fi
  url="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["package"]["url"])' "$backup/reader-after.json")"
  case "$url" in
    https://img.rongnho.shop/*)
      echo "Cache probe (no purge; HIT depends on edge location and cacheability):"
      for n in 1 2; do curl -fsSI --max-time 15 "$url" | grep -Ei '^(HTTP/|x-cache:|cache-control:|content-length:|cf-ray:)' || echo "Cache probe needs manual review"; done
      ;;
    *) echo "Unexpected package host; review manually: $url" ;;
  esac
fi
echo "Backend ready. Verify public sitemap, old/new redirects, reading and admin in production."
echo "Backups: $backup"
echo "Manual rollback: sudo docker tag $rollback_tag comicplatform-api:latest"
echo "Then: cd /srv/comicplatform/deploy && sudo docker compose --env-file ../.env up -d --no-deps --no-build backend"
