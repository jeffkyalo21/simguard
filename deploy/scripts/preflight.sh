#!/usr/bin/env bash
# Checks everything the stack needs BEFORE "docker compose up", and says what to fix.
#   deploy/scripts/preflight.sh
# Checks: deploy/.env exists and is private (chmod 600); every required variable is
# set and is not a placeholder; secrets are long enough; the ML artifacts and
# the carrier mock data folders exist and are complete; the DNS name of DOMAIN
# points at this machine; docker and memory.
# It prints no secret values. Exit 0 = ready, 1 = something to fix.
# Environment: SIMGUARD_ENV_FILE (default deploy/.env; an organisation: its deploy/orgs/<slug>.env), SIMGUARD_SKIP_DNS_CHECK=1
# to skip the DNS check, SIMGUARD_SKIP_MODE_CHECK=1 to skip the file mode checks
# (both for local tests; Windows files always look like mode 644).
set -uo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${SIMGUARD_ENV_FILE:-$DEPLOY_DIR/.env}"
fails=0
warns=0

fail() { echo "FAIL  $*" >&2; fails=$((fails + 1)); }
warn() { echo "warn  $*" >&2; warns=$((warns + 1)); }
ok() { echo "ok    $*"; }

if [[ ! -f "$ENV_FILE" ]]; then
  fail "$ENV_FILE not found. Run: cp deploy/.env.example deploy/.env && chmod 600 deploy/.env, then fill it in."
  echo "$fails problem(s). Fix them and run this again." >&2
  exit 1
fi

# File mode: group/other must not read it (it holds passwords). Skipped where stat -c is missing.
mode=""
[[ "${SIMGUARD_SKIP_MODE_CHECK:-0}" == 1 ]] || mode="$(stat -c '%a' "$ENV_FILE" 2>/dev/null || true)"
if [[ -n "$mode" ]]; then
  if [[ "$mode" =~ ^[0-7]*00$ ]]; then ok "$(basename "$ENV_FILE") mode $mode"; else fail "$ENV_FILE has mode $mode. Run: chmod 600 $ENV_FILE"; fi
fi

get() { grep -E "^$1=" "$ENV_FILE" | tail -n1 | cut -d= -f2- | tr -d '\r'; }

# A value is a placeholder when empty, CHANGE_ME, a <template>, "changeme", "example", or one repeated character.
is_placeholder() {
  local v="$1" lower
  lower="$(printf '%s' "$v" | tr '[:upper:]' '[:lower:]')"
  [[ -z "$v" ]] && return 0
  [[ "$lower" == *change_me* || "$lower" == *changeme* || "$lower" == *change-me* || "$lower" == *"<"* || "$lower" == *">"* \
     || "$lower" == *replace* || "$lower" == *example* || "$lower" == *placeholder* || "$lower" == *your-* || "$lower" == *todo* ]] && return 0
  [[ "$v" =~ ^(.)\1*$ ]] && return 0
  return 1
}

# Passwords go into a connection string and a compose file: keep them plain.
plain() { [[ "$1" =~ ^[A-Za-z0-9._~+/=-]+$ ]]; }

require() { # name, minimum length, plain (1/0)
  local name="$1" min="$2" must_plain="$3" v
  v="$(get "$name")"
  if is_placeholder "$v"; then fail "$name is missing or still a placeholder in $ENV_FILE."; return; fi
  if (( ${#v} < min )); then fail "$name is too short (${#v} characters, need $min or more)."; return; fi
  if [[ "$must_plain" == 1 ]] && ! plain "$v"; then fail "$name has characters other than letters, digits and . _ ~ + / = - (it goes into a connection string). Generate one with: openssl rand -hex 24"; return; fi
  ok "$name set"
}

require SIMGUARD_DB_NAME 1 1
require SIMGUARD_DB_OWNER 1 1
require SIMGUARD_DB_OWNER_PASSWORD 16 1
require SIMGUARD_ML_USER 1 1
require SIMGUARD_ML_PASSWORD 16 1
require JWT_KEY 32 0
require ML_SHARED_SECRET 32 0
require DOMAIN 3 0

if [[ "$(get SIMGUARD_ML_USER)" == "$(get SIMGUARD_DB_OWNER)" && -n "$(get SIMGUARD_ML_USER)" ]]; then
  fail "SIMGUARD_ML_USER must not be the database owner."
fi
if ! is_placeholder "$(get JWT_KEY)" && [[ "$(get JWT_KEY)" == "$(get ML_SHARED_SECRET)" ]]; then
  fail "JWT_KEY and ML_SHARED_SECRET must be different values."
fi
if grep -Eq "^[A-Z_]+=[\"']" "$ENV_FILE"; then
  fail "A value in $ENV_FILE is wrapped in quotes. Remove the quotes: docker compose and this script read the text as it is."
fi

# First Admin: needed only before the first account exists.
if [[ -z "$(get SIMGUARD_ADMIN_EMAIL)" ]]; then
  warn "SIMGUARD_ADMIN_EMAIL is empty: no first Admin is created (fine if accounts already exist)."
elif [[ -z "$(get SIMGUARD_ADMIN_PASSWORD)" ]]; then
  warn "SIMGUARD_ADMIN_PASSWORD is empty: no first Admin is created (fine if accounts already exist)."
fi
admin_pw="$(get SIMGUARD_ADMIN_PASSWORD)"
if [[ -n "$admin_pw" ]] && (( ${#admin_pw} < 12 )); then fail "SIMGUARD_ADMIN_PASSWORD must be 12 or more characters."; fi
if [[ "$(get SIMGUARD_RECOVER_ADMIN)" == "true" ]]; then
  warn "SIMGUARD_RECOVER_ADMIN=true: emergency Admin recovery is ON. Remove it (and the admin password) after you have signed in."
fi
if [[ -z "$(get BREVO_API_KEY)" ]]; then
  warn "BREVO_API_KEY is empty: invitations and password emails are NOT sent. Set it before inviting users (docs/deployment.md, Email)."
fi

# Data folders.
art="$(get SIMGUARD_ML_ARTIFACTS_DIR)"
if [[ -z "$art" ]]; then fail "SIMGUARD_ML_ARTIFACTS_DIR is not set."
elif [[ ! -d "$art" ]]; then fail "ML artifacts folder $art does not exist. Copy ml-service/artifacts there (scp, see docs/deployment.md)."
else
  missing=()
  for f in feature_scaler.joblib feature_fill_values.json isolation_forest.joblib cohort_stats.json xgboost_triage.joblib xgboost_feature_columns.json shap_base_value.json; do
    [[ -f "$art/$f" ]] || missing+=("$f")
  done
  if (( ${#missing[@]} )); then fail "ML artifacts folder $art is missing: ${missing[*]}"; else ok "ML artifacts complete in $art"; fi
fi

mock="$(get SIMGUARD_MOCK_DATA_DIR)"
if [[ -z "$mock" ]]; then fail "SIMGUARD_MOCK_DATA_DIR is not set."
elif [[ ! -d "$mock" ]]; then fail "Carrier mock data folder $mock does not exist. Copy deploy/data-pack/mock-data there (scp, see docs/deployment.md)."
else
  missing=()
  for f in sims.csv usage.csv sim_mapping.csv; do [[ -f "$mock/$f" ]] || missing+=("$f"); done
  if (( ${#missing[@]} )); then fail "Carrier mock data folder $mock is missing: ${missing[*]}"; else ok "carrier mock data present in $mock"; fi
  [[ -f "$mock/synthetic_cdr_sessions.csv" ]] || warn "$mock/synthetic_cdr_sessions.csv is missing: the carrier mock's per-session endpoints will not work."
fi

mockenv="$(get SIMGUARD_MOCK_ENV_FILE)"
if [[ -z "$mockenv" ]]; then fail "SIMGUARD_MOCK_ENV_FILE is not set."
elif [[ ! -f "$mockenv" ]]; then fail "Carrier keys file $mockenv does not exist. Copy deploy/data-pack/mock.env there and chmod 600 it."
else
  keys="$(grep -cE '^CarrierMock__Keys__[A-Za-z]+=.{16,}' "$mockenv" || true)"
  if (( keys < 1 )); then fail "$mockenv holds no CarrierMock__Keys__<carrier> entries. Run deploy/scripts/prepare-data.py."; else ok "$keys carrier key(s) in $(basename "$mockenv")"; fi
  mmode=""
  [[ "${SIMGUARD_SKIP_MODE_CHECK:-0}" == 1 ]] || mmode="$(stat -c '%a' "$mockenv" 2>/dev/null || true)"
  [[ -z "$mmode" || "$mmode" =~ ^[0-7]*00$ ]] || fail "$mockenv has mode $mmode. Run: chmod 600 $mockenv"
fi

# DNS: the name must point at this machine, or Let's Encrypt cannot issue the certificate.
# An organisation on a subdomain sets SIMGUARD_ORG_HOST (create-org.sh does); else DOMAIN.
domain="$(get SIMGUARD_ORG_HOST)"
[[ -n "$domain" ]] || domain="$(get DOMAIN)"
if [[ "${SIMGUARD_SKIP_DNS_CHECK:-0}" == 1 || "$domain" == localhost || "$domain" == *.localhost ]]; then
  warn "DNS check skipped for $domain."
elif [[ -n "$domain" ]]; then
  resolved=""
  if command -v getent >/dev/null 2>&1; then resolved="$(getent ahostsv4 "$domain" 2>/dev/null | awk '{print $1}' | sort -u | tr '\n' ' ')"
  elif command -v dig >/dev/null 2>&1; then resolved="$(dig +short A "$domain" | tr '\n' ' ')"
  elif command -v nslookup >/dev/null 2>&1; then resolved="$(nslookup "$domain" 2>/dev/null | awk '/^Address: /{print $2}' | tr '\n' ' ')"; fi
  me="$(curl -fsS --max-time 8 https://api.ipify.org 2>/dev/null || curl -fsS --max-time 8 https://ifconfig.me 2>/dev/null || true)"
  if [[ -z "${resolved// /}" ]]; then
    fail "DNS: $domain does not resolve. Add an A record for $domain pointing at this VM${me:+ ($me)}."
  elif [[ -z "$me" ]]; then
    fail "DNS: could not find this machine's public address (no internet?). $domain resolves to: $resolved. Set SIMGUARD_SKIP_DNS_CHECK=1 only if you are sure."
  elif [[ " $resolved " != *" $me "* ]]; then
    fail "DNS: $domain resolves to $resolved but this machine is $me. Fix the A record and wait for it to spread."
  else
    ok "DNS: $domain -> $me (this machine)"
  fi
fi

# Docker and memory.
if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
  fail "docker is not installed or this user cannot use it (add the user to the docker group)."
elif ! docker compose version >/dev/null 2>&1; then
  fail "the docker compose plugin is missing (docker compose version fails)."
else
  ok "docker and compose available"
fi
if [[ -r /proc/meminfo ]]; then
  mem_mb=$(( $(awk '/^MemTotal:/{print $2}' /proc/meminfo) / 1024 ))
  swap_mb=$(( $(awk '/^SwapTotal:/{print $2}' /proc/meminfo) / 1024 ))
  if (( mem_mb + swap_mb < 5500 )); then warn "memory ${mem_mb} MB + swap ${swap_mb} MB: the image builds (Next.js, .NET) need about 4 GB RAM plus 2 GB swap."; else ok "memory ${mem_mb} MB + swap ${swap_mb} MB"; fi
fi

echo
if (( fails > 0 )); then
  echo "$fails problem(s), $warns warning(s). Fix the FAIL lines above and run this again." >&2
  exit 1
fi
echo "Ready ($warns warning(s)). Next: deploy/scripts/edge.sh up (once per VM), then docker compose -f deploy/docker-compose.yml up -d --build"
