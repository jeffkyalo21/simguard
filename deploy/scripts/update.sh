#!/usr/bin/env bash
# Updates the running stack in place: new code, new images, same data.
#   deploy/scripts/update.sh [--no-pull] [--no-build]
#
#   1. git pull --ff-only          (skipped with --no-pull, or outside a git checkout)
#   2. preflight.sh                (stops here if .env, folders or DNS are wrong)
#   3. docker compose pull         (postgres and caddy images)
#   4. docker compose build        (backend, ML, carrier mock, web app; one at a time
#                                   so a 4 GB VM does not run out of memory; skipped with --no-build)
#   5. docker compose up -d        (only changed services restart; the backend applies
#                                   new migrations at start; the ML grants are re-applied)
#   6. waits until every service is healthy
#
# It NEVER removes volumes: no "down", no "-v", no "volume rm". The database,
# the Admin accounts in it, the Data Protection keys (carrier API keys), the
# certificates and the backups live in named volumes and survive.
# Take a backup first when a release contains migrations: deploy/scripts/backup.sh
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
here="$(dirname "${BASH_SOURCE[0]}")"
repo="$(cd "$DEPLOY_DIR/.." && pwd)"

do_pull=1
do_build=1
for arg in "$@"; do
  case "$arg" in
    --no-pull) do_pull=0 ;;
    --no-build) do_build=0 ;;
    *) echo "unknown option: $arg (use --no-pull, --no-build)" >&2; exit 2 ;;
  esac
done

if (( do_pull )) && git -C "$repo" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if [[ -n "$(git -C "$repo" status --porcelain --untracked-files=no)" ]]; then
    echo "The checkout has local changes; not pulling. Commit or stash them, or use --no-pull." >&2
    exit 1
  fi
  git -C "$repo" pull --ff-only
fi

"$here/preflight.sh"

dc pull postgres caddy dbgrants
if (( do_build )); then
  for service in backend ml carriermock frontend; do
    echo "== building $service"
    dc build --pull "$service"
  done
fi

dc up -d --remove-orphans

# Wait for every long-running service to report healthy (up to 6 minutes).
services=(postgres backend ml carriermock frontend caddy)
for _ in $(seq 1 72); do
  waiting=()
  for s in "${services[@]}"; do
    state="$(dc ps --format '{{.Health}}' "$s" 2>/dev/null | head -n1 || true)"
    [[ "$state" == "healthy" ]] || waiting+=("$s:${state:-down}")
  done
  (( ${#waiting[@]} == 0 )) && break
  sleep 5
done
if (( ${#waiting[@]} )); then
  echo "Not healthy after 6 minutes: ${waiting[*]}" >&2
  echo "Look at: docker compose -f deploy/docker-compose.yml logs --tail 50 <service>" >&2
  exit 1
fi

# The compose service dbgrants already re-applied the ML role on "up". Doing it
# again is harmless and covers a stack that predates that service.
if [[ "$(psql_owner -d "$DB_NAME" -Atc "select to_regclass('public.ml_daily_usage') is not null")" == "t" ]]; then
  "$here/apply-ml-grants.sh"
fi
echo "Update finished. All services healthy. Volumes untouched."
