#!/usr/bin/env bash
# Backs up every organisation in deploy/orgs/orgs.list, one after the other. A failure
# in one organisation does not stop the others; the exit code is 1 if any failed.
#   deploy/scripts/backup-all.sh
# Cron example: docs/deployment.md, "Backups of every organisation".
set -uo pipefail
here="$(dirname "${BASH_SOURCE[0]}")"
SIMGUARD_LIB_DEFER=1 source "$here/lib.sh"

status=0
slugs="$(org_lines | awk '{print $1}')"
if [[ -z "$slugs" ]]; then
  echo "No organisations in $ORGS_LIST; backing up the stack of deploy/.env"
  exec "$here/backup.sh"
fi
for slug in $slugs; do
  echo "== $slug"
  "$here/backup.sh" "$slug" || { echo "backup of $slug FAILED" >&2; status=1; }
done
exit $status
