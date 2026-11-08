#!/usr/bin/env bash
# Updates the running stack to the newest images in place.
# It NEVER removes volumes: no "down", no "-v", no "volume rm". The database,
# the Admin accounts in it and the backups live in named volumes and survive.
# Usage: deploy/scripts/update.sh
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
here="$(dirname "${BASH_SOURCE[0]}")"

dc pull --ignore-buildable
dc up -d --remove-orphans

# Wait for the database to report healthy before touching it.
for _ in $(seq 1 60); do
  state="$(dc ps --format '{{.Health}}' postgres 2>/dev/null | head -n1 || true)"
  [[ "$state" == "healthy" ]] && break
  sleep 2
done
[[ "${state:-}" == "healthy" ]] || { echo "postgres is not healthy" >&2; exit 1; }

# A new migration can drop and recreate ml_daily_usage, which loses the ML role's
# grant. Refresh it whenever the view exists (migrations run in the backend at startup).
if [[ "$(psql_owner -d "$DB_NAME" -Atc "select to_regclass('public.ml_daily_usage') is not null")" == "t" ]]; then
  "$here/apply-ml-grants.sh"
fi
echo "Update finished. Volumes untouched."
