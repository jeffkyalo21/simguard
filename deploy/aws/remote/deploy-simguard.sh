#!/usr/bin/env bash
# Runs ON THE LIVE SIMGUARD SERVER as root, sent by
# .github/workflows/deploy-simguard.yml through Systems Manager Run Command.
#
# Automates the documented update of the live VM (backup.sh, then pull, build,
# up -d, wait for healthy) for the application only:
#   1. checks: the checkout and deploy/.env exist, no local edits in the app folders
#   2. deploy/scripts/preflight.sh, the VM's own copy (read only)
#   3. database backup with deploy/scripts/backup.sh (the VM's own copy), and a
#      check that the new dump file exists and is not empty
#   4. fetches main and updates ONLY backend/, frontend/ and ml-service/ in the checkout
#   5. builds backend, ml and frontend one at a time, then restarts only those
#      (the ML role is re-applied after the backend, as update.sh did)
#   6. health checks: backend /health, ML /health, the front page over HTTPS
#   7. on a failure: the previous images and source come back, and the run fails
#
# It never changes, pulls or overwrites deploy/ (the live VM keeps the layout of
# commit f296146, Caddy inside that compose file), mock.env, the volumes, the
# Caddy or compose layout; it never runs "down" or "-v", never removes a volume,
# and never touches postgres, caddy or carriermock. Database migrations applied
# by a new backend are NOT rolled back (restore a backup for that: docs/deployment.md).
set -euo pipefail

REPO_DIR="${SIMGUARD_DIR:-/opt/simguard}"
STATE_DIR="/var/lib/simguard-deploy"
APP_PATHS=(backend frontend ml-service)
SERVICES=(backend ml frontend)

say() { echo "[$(date -u +%H:%M:%S)] $*"; }
fail() { echo "::error::$*" >&2; exit 1; }

[[ -d "$REPO_DIR/.git" ]] || fail "No checkout at $REPO_DIR."
[[ -f "$REPO_DIR/deploy/docker-compose.yml" && -f "$REPO_DIR/deploy/.env" ]] || fail "$REPO_DIR/deploy/docker-compose.yml or deploy/.env is missing."
cd "$REPO_DIR"

# git runs as the owner of the checkout, so no file in it becomes root's.
owner="$(stat -c %U "$REPO_DIR")"
g() { if [[ "$owner" == root ]]; then git "$@"; else sudo -u "$owner" -H git "$@"; fi; }
dc() { docker compose -f "$REPO_DIR/deploy/docker-compose.yml" "$@"; }
cfg() { grep -E "^$1=" deploy/.env | tail -n1 | cut -d= -f2- | tr -d '\r' || true; }

mkdir -p "$STATE_DIR"
# The application commit that is live. First run: the checkout's HEAD.
previous="$(cat "$STATE_DIR/app-commit" 2>/dev/null || g rev-parse HEAD)"
g cat-file -e "$previous^{commit}" 2>/dev/null || fail "Recorded commit $previous is unknown to the checkout."

# 1. Local edits in the app folders would be overwritten: stop, like update.sh did.
if ! g diff --quiet "$previous" -- "${APP_PATHS[@]}"; then
  g diff --stat "$previous" -- "${APP_PATHS[@]}" | tail -n 5
  fail "backend/, frontend/ or ml-service/ differ from the live commit ${previous:0:12} (local edits). Nothing was changed."
fi
if [[ -n "$(dc ps -a --format '{{.Service}}' | grep -x caddy || true)" ]]; then
  say "layout: the old single-VM layout with its own caddy (expected; deploy/ is left as it is)"
else
  say "layout: no caddy service in this stack (deploy/ is still left as it is)"
fi

g fetch --quiet origin main
target="$(g rev-parse FETCH_HEAD)"
say "live application commit ${previous:0:12}, main is ${target:0:12}"

if ! g show "$target:deploy/docker-compose.yml" | cmp -s - deploy/docker-compose.yml; then
  say "NOTE: deploy/docker-compose.yml on main differs from the one on this VM. Ignored: deploy/ is never changed by this workflow."
fi
if g diff --quiet "$target" -- "${APP_PATHS[@]}" && [[ "${1:-}" != force ]]; then
  say "backend/, frontend/ and ml-service/ already match main. Nothing to deploy (run the workflow by hand with force to rebuild anyway)."
  exit 0
fi

# 2. Preflight with the VM's own script.
say "preflight"
deploy/scripts/preflight.sh

# 3. Backup, then prove a new non-empty dump exists.
say "database backup"
db="$(cfg SIMGUARD_DB_NAME)"
[[ -n "$db" ]] || fail "SIMGUARD_DB_NAME is not set in deploy/.env."
newest() { dc exec -T postgres sh -c "ls -1t /backups/${db}_*.dump 2>/dev/null | head -n1" | tr -d '\r'; }
before="$(newest)"
deploy/scripts/backup.sh
after="$(newest)"
if [[ -z "$after" || "$after" == "$before" ]] || ! dc exec -T postgres test -s "$after"; then
  fail "The backup did not produce a new, non-empty dump file. Nothing was changed."
fi
say "backup verified: $after ($(dc exec -T postgres du -h "$after" | cut -f1 | tr -d '\r'))"

# Remember the images that run now, by id, for the rollback.
declare -A old_image old_name
for s in "${SERVICES[@]}"; do
  cid="$(dc ps -q "$s" | head -n1)"
  [[ -n "$cid" ]] || fail "Service $s is not running; deploy it by hand first (docs/deployment.md)."
  old_image[$s]="$(docker inspect -f '{{.Image}}' "$cid")"
  old_name[$s]="$(docker inspect -f '{{.Config.Image}}' "$cid")"
  docker tag "${old_image[$s]}" "simguard-rollback/$s:previous"
done

wait_healthy() {
  local s state waiting
  for _ in $(seq 1 72); do
    waiting=()
    for s in "$@"; do
      state="$(docker inspect -f '{{.State.Health.Status}}' "$(dc ps -q "$s" | head -n1)" 2>/dev/null || echo down)"
      [[ "$state" == healthy ]] || waiting+=("$s:$state")
    done
    (( ${#waiting[@]} == 0 )) && return 0
    sleep 5
  done
  echo "not healthy after 6 minutes: ${waiting[*]}" >&2
  return 1
}

checks() {
  local domain code
  dc exec -T backend bash -c "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\r\nHost: x\r\n\r\n' >&3 && head -n1 <&3 | grep -q ' 200 '" \
    || { echo "backend /health did not answer 200" >&2; return 1; }
  say "check: backend /health 200"
  dc exec -T ml python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8001/health', timeout=5).status == 200 else 1)" \
    || { echo "ML service /health did not answer 200" >&2; return 1; }
  say "check: ML /health 200"
  domain="$(cfg DOMAIN)"; domain="${domain:-sim-guard.app}"
  code="$(curl -s --max-time 15 -o /dev/null -w '%{http_code}' --resolve "$domain:443:127.0.0.1" "https://$domain/" || true)"
  [[ "$code" == 200 ]] || { echo "https://$domain/ answered '${code:-nothing}'" >&2; return 1; }
  say "check: https://$domain/ 200 with a valid certificate"
}

# Every step returns on failure by itself: "set -e" does not apply inside a
# function that is called on the left of "||".
start_services() {
  dc up -d --no-deps --no-build backend || return 1
  wait_healthy backend || return 1
  # A migration can recreate the view the ML role reads; re-apply the grant (as update.sh did).
  if [[ "$(dc exec -T postgres psql -U "$(cfg SIMGUARD_DB_OWNER)" -d "$db" -Atc "select to_regclass('public.ml_daily_usage') is not null" | tr -d '\r')" == t ]]; then
    deploy/scripts/apply-ml-grants.sh || return 1
  fi
  dc up -d --no-deps --no-build ml frontend || return 1
  wait_healthy "${SERVICES[@]}"
}

rollback() {
  local why="$1"
  echo "::error::$why" >&2
  say "rolling back the application images to the previous version"
  for s in "${SERVICES[@]}"; do docker tag "${old_image[$s]}" "${old_name[$s]}"; done
  g restore --source="$previous" --staged --worktree -- "${APP_PATHS[@]}" || true
  if start_services && checks; then
    fail "Deploy of ${target:0:12} failed ($why). The previous images (${previous:0:12}) are running and healthy again. Database migrations applied by the new backend were NOT rolled back."
  fi
  fail "Deploy of ${target:0:12} failed ($why) and the rollback is not healthy either. Follow the rollback guide in docs/deployment.md (restore the backup $after)."
}

# 4. Only the application source changes.
say "updating backend/, frontend/, ml-service/ to ${target:0:12} (deploy/ untouched)"
g restore --source="$target" --staged --worktree -- "${APP_PATHS[@]}"

# 5. Build one at a time (a 4 GB VM), then restart only these services.
for s in "${SERVICES[@]}"; do
  say "building $s"
  if ! dc build --pull "$s"; then
    for r in "${SERVICES[@]}"; do docker tag "${old_image[$r]}" "${old_name[$r]}"; done
    g restore --source="$previous" --staged --worktree -- "${APP_PATHS[@]}" || true
    fail "Building $s failed. Nothing was restarted; the source is back at ${previous:0:12}."
  fi
done

say "restarting backend, then ml and frontend"
start_services || rollback "the services did not become healthy"

# 6. Health checks.
checks || rollback "a health check failed"

echo "$previous" > "$STATE_DIR/previous-commit"
echo "$target" > "$STATE_DIR/app-commit"
say "deploy finished: application ${target:0:12} is live (backup $after)"
