#!/usr/bin/env bash
# Registers a stack that already runs (the live one, started before several
# organisations existed) so the shared Caddy and the scripts know it.
#   deploy/scripts/adopt-org.sh <slug> <compose project> <env file> <host>
# Example (the live stack, project "simguard", settings in deploy/.env):
#   deploy/scripts/adopt-org.sh main simguard /opt/simguard/deploy/.env sim-guard.app
# It changes nothing about the running stack; it only writes deploy/orgs/orgs.list.
# Then run deploy/scripts/edge.sh up (docs/deployment.md, "Moving the live stack").
set -euo pipefail
here="$(dirname "${BASH_SOURCE[0]}")"
SIMGUARD_LIB_DEFER=1 source "$here/lib.sh"
if [[ $# -ne 4 ]]; then echo "Usage: adopt-org.sh <slug> <compose project> <env file> <host>" >&2; exit 2; fi
slug="$1"; project="$2"; env_file="$3"; host="$4"
[[ "$slug" =~ ^[a-z][a-z0-9-]{0,19}$ ]] || { echo "bad slug" >&2; exit 2; }
[[ "$project" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || { echo "bad project name" >&2; exit 2; }
[[ "$host" =~ ^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$ ]] || { echo "bad host" >&2; exit 2; }
[[ -f "$env_file" ]] || { echo "env file $env_file not found" >&2; exit 1; }
[[ -z "$(org_line "$slug")" ]] || { echo "organisation $slug is already registered" >&2; exit 1; }
if [[ -z "$(docker ps -aq --filter "label=com.docker.compose.project=$project" 2>/dev/null | head -n1)" ]]; then
  echo "warning: no containers of project '$project' found here." >&2
fi
mkdir -p "$ORGS_DIR"
if [[ ! -f "$ORGS_LIST" ]]; then
  echo "# slug project env-file host (one organisation per line; the first is the apex domain)" > "$ORGS_LIST"
fi
printf '%s %s %s %s\n' "$slug" "$project" "$(native "$env_file")" "$host" >> "$ORGS_LIST"
echo "Registered $slug ($project) at https://$host in $ORGS_LIST"
