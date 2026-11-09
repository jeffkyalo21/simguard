#!/usr/bin/env bash
# Stops one organisation and takes it off the shared Caddy. It asks before it deletes
# anything permanent.
#   deploy/scripts/remove-org.sh <slug>          stop it, remove its Caddy entry, ASK about its volumes
#   deploy/scripts/remove-org.sh --force <slug>  same, but delete its volumes, env file and folder without asking (for a
#                                                stack that never became healthy after create-org.sh)
# Volumes hold the database, the Data Protection keys and the backups of that organisation.
# Deleting them cannot be undone. Its folder in /srv/simguard/<slug> and its env file are
# kept unless you also answer yes to the last question. With no terminal every question is
# answered no. The first organisation cannot be removed while others exist.
set -euo pipefail
here="$(dirname "${BASH_SOURCE[0]}")"
SIMGUARD_LIB_DEFER=1 source "$here/lib.sh"

force=0
if [[ "${1:-}" == "--force" ]]; then force=1; shift; fi
slug="${1:-}"
if [[ -z "$slug" || $# -ne 1 ]]; then echo "Usage: remove-org.sh [--force] <slug>" >&2; exit 2; fi
[[ "$slug" =~ ^[a-z][a-z0-9-]*$ ]] || { echo "bad slug" >&2; exit 2; }

line="$(org_line "$slug")"
if [[ -n "$line" ]]; then
  read -r _ project env_file host <<<"$line"
  if [[ "$slug" == "$(first_org_slug)" && "$(org_lines | wc -l | tr -d ' ')" -gt 1 ]]; then
    echo "$slug is the first organisation (apex domain). Remove the others first." >&2
    exit 1
  fi
else
  env_file="$ORGS_DIR/$slug.env"
  [[ -f "$env_file" ]] || { echo "No organisation '$slug'." >&2; exit 1; }
  project="$(grep -E '^COMPOSE_PROJECT_NAME=' "$env_file" | tail -n1 | cut -d= -f2- | tr -d '\r')"
  host=""
fi
[[ "$project" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || { echo "bad project name in $env_file" >&2; exit 1; }
dcp() { docker compose -p "$project" --env-file "$(native "$env_file")" -f "$(native "$DEPLOY_DIR/docker-compose.yml")" "$@"; }

ask() { # question; yes only when the answer is exactly "yes"; no terminal means no
  local reply=""
  if [[ ! -t 0 ]]; then echo "$1 (no terminal: answered no)"; return 1; fi
  read -r -p "$1 Type yes to confirm: " reply
  [[ "$reply" == "yes" ]]
}

echo "Stopping $project${host:+ ($host)}"
# "down" without -v: containers and the private network go, the volumes survive this step.
dcp down --remove-orphans >/dev/null 2>&1 || true

if [[ -n "$line" ]]; then
  tmp="$(mktemp)"
  org_lines | awk -v s="$slug" '$1 != s' > "$tmp"
  { echo "# slug project env-file host (one organisation per line; the first is the apex domain)"; cat "$tmp"; } > "$ORGS_LIST"
  rm -f "$tmp"
  if "$here/edge.sh" reload >/dev/null 2>&1; then
    echo "Removed the Caddy entry."
  else
    echo "Caddy was not reloaded (is the shared Caddy running? deploy/scripts/edge.sh up)." >&2
  fi
fi

# Only this project's own volumes (<project>_pgdata and so on), never a named shared one.
volumes="$(docker volume ls -q --filter "label=com.docker.compose.project=$project" | grep -E "^${project}_" || true)"
if [[ -n "$volumes" ]]; then
  echo "Volumes of $project (database, keys, backups):"
  sed 's/^/  /' <<<"$volumes"
  if (( force )) || ask "Delete these volumes? All data of this organisation is lost."; then
    for v in $volumes; do
      if docker volume rm "$v" >/dev/null 2>&1; then echo "  deleted $v"; else echo "  kept $v (in use, for example by the shared Caddy)"; fi
    done
  else
    echo "Volumes kept."
  fi
fi

org_dir="$(dirname "$(grep -E '^SIMGUARD_MOCK_ENV_FILE=' "$env_file" | tail -n1 | cut -d= -f2- | tr -d '\r')")"
if (( force )) || ask "Also delete the env file $env_file and the folder $org_dir (mock data, carrier keys, model copies)?"; then
  rm -rf -- "$env_file" "$org_dir"
  echo "Env file and folder deleted."
else
  echo "Env file and folder kept."
fi
