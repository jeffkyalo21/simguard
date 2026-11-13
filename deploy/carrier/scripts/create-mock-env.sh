#!/usr/bin/env bash
# Creates /srv/carrier/mock.env on the carrier host with new random keys:
# one per carrier in the data (sims.csv) and MOCK_ADMIN_KEY. Mode 600, owner
# root. Never overwrites an existing file. Prints no key.
#   sudo deploy/carrier/scripts/create-mock-env.sh
# Read one key only when you type it into SimGuard Settings (paste it nowhere else):
#   sudo grep '^CarrierMock__Keys__Safaricom=' /srv/carrier/mock.env | cut -d= -f2
set -euo pipefail
file="${CARRIER_MOCK_ENV_FILE:-/srv/carrier/mock.env}"
sims="${CARRIER_DATA_DIR:-/srv/carrier/mock-data}/sims.csv"

if [[ $EUID -ne 0 ]]; then echo "Run with sudo." >&2; exit 2; fi
if [[ -e "$file" ]]; then
  echo "$file already exists; nothing changed. Delete it only if you want new keys (then re-enter them in SimGuard)." >&2
  exit 1
fi
if [[ ! -f "$sims" ]]; then
  echo "$sims not found. Build the data folder first: deploy/carrier/scripts/build-mock-data.sh" >&2
  exit 1
fi

col="$(head -n1 "$sims" | tr -d '\r' | tr ',' '\n' | grep -nx carrier | cut -d: -f1)"
if [[ -z "$col" ]]; then echo "$sims has no carrier column." >&2; exit 1; fi
mapfile -t carriers < <(tail -n +2 "$sims" | cut -d, -f"$col" | tr -d '\r' | sort -u)

umask 077
tmp="$(mktemp "$(dirname "$file")/.mock.env.XXXXXX")"
{
  echo "# Created by create-mock-env.sh on $(date -u +%F). Never commit or copy this file."
  for c in "${carriers[@]}"; do echo "CarrierMock__Keys__${c}=$(openssl rand -hex 24)"; done
  echo "MOCK_ADMIN_KEY=$(openssl rand -hex 24)"
  echo "# Optional settings, empty = default (see deploy/carrier/mock.env.example)."
  echo "MOCK_START_DAY="
  echo "MOCK_RELEASE_TIME="
  echo "MOCK_TIMEZONE="
  echo "MOCK_DATE_SHIFT_DAYS="
  echo "MOCK_SCHEDULER_ENABLED="
} > "$tmp"
chmod 600 "$tmp"
mv "$tmp" "$file"
echo "Wrote $file (mode 600) with keys for: ${carriers[*]} and MOCK_ADMIN_KEY. No key was printed."
