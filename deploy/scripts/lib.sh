#!/usr/bin/env bash
# Shared helpers for the deploy scripts. Source it; do not run it.
# Needs docker (with the compose plugin) and bash. Run from anywhere.

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Git Bash on Windows (local proof only) needs native paths for docker; on Ubuntu this is a no-op.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }
COMPOSE_FILES=(-f "$(native "$DEPLOY_DIR/docker-compose.yml")")
# Optional extra compose files, space separated (for example the local override).
for f in ${SIMGUARD_COMPOSE_EXTRA:-}; do COMPOSE_FILES+=(-f "$(native "$f")"); done
# Optional env file and project name (otherwise deploy/.env and COMPOSE_PROJECT_NAME).
ENV_ARGS=()
if [[ -n "${SIMGUARD_ENV_FILE:-}" ]]; then ENV_ARGS=(--env-file "$(native "$SIMGUARD_ENV_FILE")"); fi
PROJECT_ARGS=()
if [[ -n "${COMPOSE_PROJECT_NAME:-}" ]]; then PROJECT_ARGS=(-p "$COMPOSE_PROJECT_NAME"); fi

dc() { docker compose "${PROJECT_ARGS[@]}" "${ENV_ARGS[@]}" "${COMPOSE_FILES[@]}" "$@"; }

# Reads KEY from the env file (SIMGUARD_ENV_FILE or deploy/.env), or the environment.
cfg() {
  local key="$1" file="${SIMGUARD_ENV_FILE:-$DEPLOY_DIR/.env}" value=""
  if [[ -n "${!key:-}" ]]; then printf '%s' "${!key}"; return; fi
  if [[ -f "$file" ]]; then
    value="$(grep -E "^${key}=" "$file" | tail -n1 | cut -d= -f2- | tr -d '\r')"
  fi
  printf '%s' "$value"
}

DB_NAME="$(cfg SIMGUARD_DB_NAME)"
DB_OWNER="$(cfg SIMGUARD_DB_OWNER)"
if [[ -z "$DB_NAME" || -z "$DB_OWNER" ]]; then
  echo "SIMGUARD_DB_NAME and SIMGUARD_DB_OWNER must be set in deploy/.env" >&2
  exit 2
fi

# psql inside the postgres container as the owner (local socket, no password).
psql_owner() { dc exec -T postgres psql -v ON_ERROR_STOP=1 -U "$DB_OWNER" "$@"; }
