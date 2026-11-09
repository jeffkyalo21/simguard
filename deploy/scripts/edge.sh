#!/usr/bin/env bash
# The shared Caddy (deploy/shared/docker-compose.yml): the only thing on the VM
# that publishes ports 80 and 443.
#   deploy/scripts/edge.sh network  only create the edge network (the migration needs it before Caddy moves)
#   deploy/scripts/edge.sh up       create the edge network, render the sites, start Caddy
#   deploy/scripts/edge.sh reload   render the sites and reload Caddy without a restart
#   deploy/scripts/edge.sh ensure   up when Caddy is not running, else reload (what create-org.sh uses)
#   deploy/scripts/edge.sh down     stop Caddy (certificates stay in their volume)
#   deploy/scripts/edge.sh status   container state
# Settings (HTTP_PORT, HTTPS_PORT, CADDY_GLOBAL_OPTIONS, SIMGUARD_CADDY_DATA_VOLUME)
# come from SIMGUARD_EDGE_ENV_FILE, else deploy/orgs/edge.env, else deploy/.env.
set -euo pipefail
here="$(dirname "${BASH_SOURCE[0]}")"
SIMGUARD_LIB_DEFER=1 source "$here/lib.sh"

edge_env="${SIMGUARD_EDGE_ENV_FILE:-}"
[[ -n "$edge_env" ]] || { [[ -f "$ORGS_DIR/edge.env" ]] && edge_env="$ORGS_DIR/edge.env" || true; }
[[ -n "$edge_env" ]] || { [[ -f "$DEPLOY_DIR/.env" ]] && edge_env="$DEPLOY_DIR/.env" || true; }
envval() { [[ -f "$1" ]] && grep -E "^$2=" "$1" | tail -n1 | cut -d= -f2- | tr -d '
' || true; }
edge_project="${SIMGUARD_EDGE_PROJECT:-}"
if [[ -z "$edge_project" && -n "$edge_env" ]]; then edge_project="$(envval "$edge_env" SIMGUARD_EDGE_PROJECT)"; fi
edge_project="${edge_project:-simguard-edge}"
if [[ -z "${SIMGUARD_EDGE_NETWORK:-}" && -n "$edge_env" ]]; then
  net="$(envval "$edge_env" SIMGUARD_EDGE_NETWORK)"
  if [[ -n "$net" ]]; then EDGE_NETWORK="$net"; fi
fi
# -p wins over a COMPOSE_PROJECT_NAME left in the environment by another script.
EDGE_ARGS=(-p "$edge_project" -f "$(native "$DEPLOY_DIR/shared/docker-compose.yml")")
[[ -z "$edge_env" ]] || EDGE_ARGS=(--env-file "$(native "$edge_env")" "${EDGE_ARGS[@]}")
export SIMGUARD_CADDY_SITES_DIR="${SIMGUARD_CADDY_SITES_DIR:-$(native "$ORGS_DIR/caddy")}"
edc() { docker compose "${EDGE_ARGS[@]}" "$@"; }

running() { [[ -n "$(edc ps -q --status running caddy 2>/dev/null)" ]]; }

reload() {
  "$here/render-caddy.sh" >/dev/null
  edc exec -T caddy caddy validate --config /etc/caddy/Caddyfile >/dev/null
  edc exec -T caddy caddy reload --config /etc/caddy/Caddyfile >/dev/null
  echo "Caddy reloaded."
}

up() {
  ensure_edge_network
  "$here/render-caddy.sh" >/dev/null
  edc up -d
  for _ in $(seq 1 30); do
    [[ "$(edc ps --format '{{.Health}}' caddy | head -n1)" == healthy ]] && { echo "Caddy is up."; return 0; }
    sleep 2
  done
  echo "Caddy is not healthy: edc logs caddy" >&2; return 1
}

case "${1:-}" in
  network) ensure_edge_network; echo "Network $EDGE_NETWORK is ready." ;;
  up) up ;;
  reload) reload ;;
  ensure) if running; then reload; else up; fi ;;
  down) edc down ;;
  status) edc ps ;;
  *) echo "Usage: edge.sh network|up|reload|ensure|down|status" >&2; exit 2 ;;
esac
