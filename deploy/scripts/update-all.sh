#!/usr/bin/env bash
# Updates every organisation: git pull and the image builds happen once (the images are
# shared), then each stack is brought up on them in turn. Volumes are never touched.
#   deploy/scripts/update-all.sh [--no-pull] [--no-build]
set -euo pipefail
here="$(dirname "${BASH_SOURCE[0]}")"
SIMGUARD_LIB_DEFER=1 source "$here/lib.sh"
slugs="$(org_lines | awk '{print $1}')"
[[ -n "$slugs" ]] || { echo "No organisations in $ORGS_LIST" >&2; exit 1; }

first=1
for slug in $slugs; do
  echo "== $slug"
  if (( first )); then
    SIMGUARD_ORG="$slug" "$here/update.sh" "$@"
  else
    SIMGUARD_ORG="$slug" "$here/update.sh" --no-pull --no-build
  fi
  first=0
done
