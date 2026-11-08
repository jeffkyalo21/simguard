#!/usr/bin/env bash
# Compressed pg_dump of the SimGuard database into the backup volume
# (/backups in the postgres container), keeping the newest 7.
# Usage: deploy/scripts/backup.sh          (run daily from cron, see docs/deployment.md)
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
KEEP="${SIMGUARD_BACKUP_KEEP:-7}"

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
name="${DB_NAME}_${stamp}.dump"

# Write to a temporary name first so a half written dump is never listed.
dc exec -T postgres sh -c "pg_dump -U '$DB_OWNER' -d '$DB_NAME' -Fc -Z 6 -f '/backups/.$name.part' && mv '/backups/.$name.part' '/backups/$name'"

# Keep the newest $KEEP dumps of this database.
dc exec -T postgres sh -c "ls -1t /backups/${DB_NAME}_*.dump 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -f --"

size="$(dc exec -T postgres sh -c "du -h '/backups/$name' | cut -f1")"
echo "Backup written: $name ($size). Kept: $(dc exec -T postgres sh -c "ls -1 /backups/${DB_NAME}_*.dump | wc -l")"
