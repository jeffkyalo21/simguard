#!/usr/bin/env bash
# Shared helpers for the carrier host scripts. Source it; do not run it.
# The scripts run on the carrier server as root (sudo), because mock.env is
# readable by root only. They call the mock on loopback (127.0.0.1:8081), never
# through Caddy, and never print a key.
set -euo pipefail

CARRIER_ENV_FILE="${CARRIER_MOCK_ENV_FILE:-/srv/carrier/mock.env}"
MOCK_URL="${MOCK_URL:-http://127.0.0.1:8081}"

# Value of KEY in mock.env (empty when missing).
env_value() {
  grep -E "^$1=" "$CARRIER_ENV_FILE" | tail -n1 | cut -d= -f2- | tr -d '\r' || true
}

# Writes the admin header into a private temporary file, so the key never
# appears in a command line (ps) or in the output. Removed on exit.
_header_file=""
_body_file=""
cleanup() { rm -f "$_header_file" "$_body_file"; }
trap cleanup EXIT

admin_header_file() {
  if [[ ! -r "$CARRIER_ENV_FILE" ]]; then
    echo "Cannot read $CARRIER_ENV_FILE. Run this script with sudo." >&2
    exit 2
  fi
  _header_file="$(mktemp)"
  chmod 600 "$_header_file"
  local admin carrier
  admin="$(env_value MOCK_ADMIN_KEY)"
  if [[ -n "$admin" ]]; then
    printf 'X-Admin-Key: %s\n' "$admin" > "$_header_file"
  else
    # Without MOCK_ADMIN_KEY the mock accepts any carrier key on the admin routes.
    carrier="$(grep -E '^CarrierMock__Keys__[^=]+=.' "$CARRIER_ENV_FILE" | head -n1 | cut -d= -f2- | tr -d '\r' || true)"
    if [[ -z "$carrier" ]]; then
      echo "$CARRIER_ENV_FILE has neither MOCK_ADMIN_KEY nor a carrier key." >&2
      exit 2
    fi
    printf 'X-Api-Key: %s\n' "$carrier" > "$_header_file"
  fi
}

# mock_admin METHOD PATH [JSON body]: prints the response, fails on a non 2xx status.
mock_admin() {
  local method="$1" path="$2" body="${3:-}" status
  local data='{}'
  [[ -n "$body" ]] && data="$body"
  admin_header_file
  _body_file="$(mktemp)"
  local args=(-sS -o "$_body_file" -w '%{http_code}' -H @"$_header_file")
  if [[ "$method" == POST ]]; then
    args+=(-X POST -H 'Content-Type: application/json' --data "$data")
  fi
  if ! status="$(curl "${args[@]}" "$MOCK_URL$path")"; then
    echo "The mock at $MOCK_URL does not answer. Is it running? (docker ps)" >&2
    exit 1
  fi
  if command -v python3 >/dev/null 2>&1; then
    python3 -m json.tool < "$_body_file" 2>/dev/null || cat "$_body_file"
  else
    cat "$_body_file"; echo
  fi
  if [[ "$status" != 2* ]]; then
    echo "The mock answered $status." >&2
    exit 1
  fi
}
