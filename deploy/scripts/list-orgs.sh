#!/usr/bin/env bash
# One line per organisation: slug, URL, container health, first Admin sign-in state.
#   deploy/scripts/list-orgs.sh
set -uo pipefail
SIMGUARD_LIB_DEFER=1 source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

lines="$(org_lines)"
if [[ -z "$lines" ]]; then echo "No organisations yet. Create one: deploy/scripts/create-org.sh <slug> <admin-email>"; exit 0; fi

printf '%-14s %-38s %-10s %s\n' ORGANISATION URL HEALTH SERVICES
while read -r slug project envfile host; do
  states="$(docker compose -p "$project" --env-file "$(native "$envfile")" -f "$(native "$DEPLOY_DIR/docker-compose.yml")" ps --format '{{.Service}}={{.Health}}' 2>/dev/null | grep -v '^dbgrants=' || true)"
  if [[ -z "$states" ]]; then health="stopped"; summary="-"
  elif grep -qvE '=healthy$' <<<"$states"; then health="unhealthy"; summary="$(grep -vE '=healthy$' <<<"$states" | tr '\n' ' ')"
  else health="healthy"; summary="$(grep -c . <<<"$states") services"; fi
  printf '%-14s %-38s %-10s %s\n' "$slug" "https://$host" "$health" "$summary"
done <<<"$lines"
