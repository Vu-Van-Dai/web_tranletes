#!/usr/bin/env sh
# Daily logical backup of the platform database (run from cron on the Oracle host):
#   0 3 * * * /srv/comicplatform/deploy/scripts/backup-postgres.sh >> /var/log/comicplatform-backup.log 2>&1
# Keeps 14 days locally; copy /data/backups off-host (e.g. rclone to a separate R2 bucket).
set -eu
cd "$(dirname "$0")/.."
BACKUP_DIR="${BACKUP_DIR:-/data/backups}"
KEEP_DAYS="${KEEP_DAYS:-14}"
mkdir -p "$BACKUP_DIR"
file="$BACKUP_DIR/comicplatform-$(date -u +%Y%m%dT%H%M%SZ).dump"
docker compose exec -T postgres pg_dump -U "${DB_USER:-comicplatform}" -d "${DB_NAME:-comicplatform}" -Fc > "$file"
echo "$(date -u +%FT%TZ) backup written: $file ($(du -h "$file" | cut -f1))"
find "$BACKUP_DIR" -name 'comicplatform-*.dump' -mtime +"$KEEP_DAYS" -delete
