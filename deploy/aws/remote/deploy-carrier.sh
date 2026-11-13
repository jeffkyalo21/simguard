#!/usr/bin/env bash
# Runs ON THE CARRIER SERVER as root, sent by .github/workflows/deploy-carrier.yml
# through Systems Manager Run Command (deploy/aws/ssm-run.sh).
#
#   1. fetches the latest main into the read-only clone /opt/simguard-carrier
#   2. docker compose up -d --build for deploy/carrier only (mock and Caddy)
#   3. health check: the mock on loopback, then Caddy with its certificate
#   4. on a failed check: back to the previous commit, rebuild, and fail
#
# It never reads, writes or deletes /srv/carrier/mock.env, /srv/carrier/mock-data
# or /srv/carrier/state (the pointer file), never runs "down" or "-v", and never
# contacts the SimGuard server.
set -euo pipefail

REPO_DIR="${CARRIER_REPO_DIR:-/opt/simguard-carrier}"
COMPOSE_FILE="deploy/carrier/docker-compose.yml"
PROJECT="simguard-carrier"
DOMAIN="${CARRIER_DOMAIN:-carrier.sim-guard.app}"
export GIT_LFS_SKIP_SMUDGE=1

say() { echo "[$(date -u +%H:%M:%S)] $*"; }
dc() { docker compose -p "$PROJECT" -f "$REPO_DIR/$COMPOSE_FILE" "$@"; }

# Waits for the mock container to be healthy and both checks to pass (up to 5 minutes;
# the first certificate from Let's Encrypt can take a minute).
healthy() {
  local code mock state
  for _ in $(seq 1 30); do
    state="$(docker inspect -f '{{.State.Health.Status}}' "$(dc ps -q mock 2>/dev/null | head -n1)" 2>/dev/null || echo missing)"
    if [[ "$state" == healthy ]]; then
      mock="$(curl -s --max-time 5 http://127.0.0.1:8081/ || true)"
      code="$(curl -s --max-time 10 -o /dev/null -w '%{http_code}' --resolve "$DOMAIN:443:127.0.0.1" "https://$DOMAIN/" || true)"
      if [[ "$mock" == *'"sims":'* && "$code" == 200 ]]; then
        say "health: mock healthy, $mock"
        say "health: https://$DOMAIN/ through Caddy answered 200 with a valid certificate"
        return 0
      fi
      say "waiting: mock $state, https://$DOMAIN/ answered '${code:-nothing}'"
    else
      say "waiting: mock container is $state"
    fi
    sleep 10
  done
  return 1
}

for f in /srv/carrier/mock.env /srv/carrier/mock-data/usage.csv; do
  if [[ ! -e "$f" ]]; then
    echo "::error::$f is missing on the carrier server. Do the host steps of docs/deploy-checklist.md first." >&2
    exit 1
  fi
done
if [[ ! -d "$REPO_DIR/.git" ]]; then
  echo "::error::No clone at $REPO_DIR. Run deploy/aws/carrier/setup.sh first." >&2
  exit 1
fi

cd "$REPO_DIR"
previous="$(git rev-parse HEAD)"
git fetch --quiet origin main
target="$(git rev-parse FETCH_HEAD)"
say "previous commit ${previous:0:12}, deploying main ${target:0:12}"
git checkout --quiet --force --detach "$target"

say "building and starting the carrier bundle (mock and Caddy only)"
if dc up -d --build --remove-orphans && healthy; then
  say "deploy finished: ${target:0:12} is live"
  exit 0
fi

echo "::error::Health check failed for ${target:0:12}." >&2
dc ps || true
dc logs --tail 40 mock caddy || true

if [[ "$previous" == "$target" ]]; then
  echo "::error::The previous commit is the same one; nothing to roll back to. Look at the logs above." >&2
  exit 1
fi

say "rolling back to ${previous:0:12}"
git checkout --quiet --force --detach "$previous"
if dc up -d --build --remove-orphans && healthy; then
  echo "::error::Deploy of ${target:0:12} failed its health check. Rolled back to ${previous:0:12}, which is healthy again." >&2
else
  echo "::error::Deploy of ${target:0:12} failed, and the rollback to ${previous:0:12} is not healthy either. The carrier feed is down: see docs/carrier-server.md." >&2
fi
exit 1
