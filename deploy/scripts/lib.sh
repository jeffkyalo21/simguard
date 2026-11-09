#!/usr/bin/env bash
# Shared helpers for the deploy scripts. Source it; do not run it.
# Needs docker (with the compose plugin) and bash. Run from anywhere.
#
# Which organisation a script works on, in this order:
#   1. SIMGUARD_ORG=<slug>        (scripts that take a slug argument set this for you)
#   2. SIMGUARD_ENV_FILE=<file>   (an env file chosen by hand; project name from the file)
#   3. the first line of deploy/orgs/orgs.list (the first organisation)
#   4. deploy/.env                (a single stack that was never registered)

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Git Bash on Windows (local proof only) needs native paths for docker; on Ubuntu this is a no-op.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

ORGS_DIR="${SIMGUARD_ORGS_DIR:-$DEPLOY_DIR/orgs}"
ORGS_LIST="$ORGS_DIR/orgs.list"
EDGE_NETWORK="${SIMGUARD_EDGE_NETWORK:-simguard-edge}"

# orgs.list: one organisation per line, blank lines and # comments ignored:
#   <slug> <compose project> <env file> <host>
# The first line is the organisation at the apex domain.
org_lines() { [[ -f "$ORGS_LIST" ]] && grep -Ev '^[[:space:]]*(#|$)' "$ORGS_LIST" | tr -d '\r' || true; }
first_org_slug() { org_lines | head -n1 | awk '{print $1}'; }
org_line() { org_lines | awk -v s="$1" '$1 == s { print; exit }'; }

# Picks the organisation (see the order above) and sets COMPOSE_PROJECT_NAME,
# SIMGUARD_ENV_FILE, the dc helper arguments, DB_NAME and DB_OWNER. Runs when this
# file is sourced; create-org.sh sets SIMGUARD_LIB_DEFER=1 and calls it later.
lib_setup() {
  local line=""
  if [[ -n "${SIMGUARD_ORG:-}" ]]; then
    line="$(org_line "$SIMGUARD_ORG")"
    if [[ -z "$line" ]]; then echo "No organisation '$SIMGUARD_ORG' in $ORGS_LIST (deploy/scripts/list-orgs.sh shows them)." >&2; exit 2; fi
  elif [[ -z "${SIMGUARD_ENV_FILE:-}" ]]; then
    line="$(org_lines | head -n1)"
  fi
  if [[ -n "$line" ]]; then
    read -r SIMGUARD_ORG_RESOLVED COMPOSE_PROJECT_NAME SIMGUARD_ENV_FILE SIMGUARD_ORG_HOST_RESOLVED <<<"$line"
    export COMPOSE_PROJECT_NAME SIMGUARD_ENV_FILE
  fi

  COMPOSE_FILES=(-f "$(native "$DEPLOY_DIR/docker-compose.yml")")
  # Optional extra compose files, space separated.
  for f in ${SIMGUARD_COMPOSE_EXTRA:-}; do COMPOSE_FILES+=(-f "$(native "$f")"); done
  # Optional env file and project name (otherwise deploy/.env and COMPOSE_PROJECT_NAME).
  ENV_ARGS=()
  if [[ -n "${SIMGUARD_ENV_FILE:-}" ]]; then ENV_ARGS=(--env-file "$(native "$SIMGUARD_ENV_FILE")"); fi
  PROJECT_ARGS=()
  if [[ -n "${COMPOSE_PROJECT_NAME:-}" ]]; then PROJECT_ARGS=(-p "$COMPOSE_PROJECT_NAME"); fi

  DB_NAME="$(cfg SIMGUARD_DB_NAME)"
  DB_OWNER="$(cfg SIMGUARD_DB_OWNER)"
  if [[ -z "$DB_NAME" || -z "$DB_OWNER" ]]; then
    echo "SIMGUARD_DB_NAME and SIMGUARD_DB_OWNER must be set in ${SIMGUARD_ENV_FILE:-deploy/.env}" >&2
    exit 2
  fi
}

dc() { docker compose "${PROJECT_ARGS[@]}" "${ENV_ARGS[@]}" "${COMPOSE_FILES[@]}" "$@"; }

# The shared network every stack's frontend joins and the shared Caddy sits on.
ensure_edge_network() {
  docker network inspect "$EDGE_NETWORK" >/dev/null 2>&1 || docker network create "$EDGE_NETWORK" >/dev/null
}

# Reads KEY from the env file (SIMGUARD_ENV_FILE or deploy/.env), or the environment.
cfg() {
  local key="$1" file="${SIMGUARD_ENV_FILE:-$DEPLOY_DIR/.env}" value=""
  if [[ -n "${!key:-}" ]]; then printf '%s' "${!key}"; return; fi
  if [[ -f "$file" ]]; then
    value="$(grep -E "^${key}=" "$file" | tail -n1 | cut -d= -f2- | tr -d '\r')"
  fi
  printf '%s' "$value"
}

# psql inside the postgres container as the owner (local socket, no password).
psql_owner() { dc exec -T postgres psql -v ON_ERROR_STOP=1 -U "$DB_OWNER" "$@"; }

[[ "${SIMGUARD_LIB_DEFER:-0}" == 1 ]] || lib_setup
