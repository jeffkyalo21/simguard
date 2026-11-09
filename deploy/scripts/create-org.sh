#!/usr/bin/env bash
# Creates one more SimGuard organisation on this VM and starts it.
#   deploy/scripts/create-org.sh <slug> <first-admin-email>
#
#   slug   lower case letters, digits and hyphens, 1 to 20 characters, starting with a
#          letter. It becomes the subdomain <slug>.DOMAIN, the project simguard-<slug>
#          and the folder /srv/simguard/<slug>. The FIRST organisation on a VM
#          is served at the apex domain (DOMAIN) instead of a subdomain.
#
# What it does: checks the inputs, creates /srv/simguard/<slug>/ (mock data, model
# artifacts, carrier keys), generates every secret with openssl (nothing is printed),
# writes deploy/orgs/<slug>.env (mode 600, git-ignored), starts the stack, waits
# until it is healthy, then adds the Caddy site entry and reloads Caddy.
# It prints only the URL and one line saying where the first Admin password is.
#
# Settings, from the environment or deploy/orgs/defaults.env (mode 600):
#   DOMAIN                        the base domain (else DOMAIN in deploy/.env). Required.
#   SIMGUARD_ORGS_ROOT            data root (default /srv/simguard)
#   SIMGUARD_MOCK_SOURCE          mock data to copy (default <root>/mock-data, else deploy/data-pack/mock-data)
#   SIMGUARD_ML_ARTIFACTS_SOURCE  model files to copy (default <root>/ml-artifacts, else ml-service/artifacts)
#   SIMGUARD_IMAGE_PREFIX         image name prefix, shared by all organisations (default simguard)
#   SIMGUARD_EDGE_NETWORK         name of the shared network (default simguard-edge); must match edge.env
#   SIMGUARD_PROJECT_PREFIX       compose project prefix: <prefix>-<slug> (default simguard)
#   BREVO_API_KEY, BREVO_SENDER_EMAIL   email settings (default: none, no email is sent)
#   SIMGUARD_ORG_VERBOSE=1        show progress and the docker output
set -Eeuo pipefail
here="$(dirname "${BASH_SOURCE[0]}")"
SIMGUARD_LIB_DEFER=1 source "$here/lib.sh"
repo="$(cd "$DEPLOY_DIR/.." && pwd)"

if [[ $# -ne 2 ]]; then
  echo "Usage: create-org.sh <slug> <first-admin-email>" >&2
  exit 2
fi
slug="$1"
admin_email="$2"

verbose="${SIMGUARD_ORG_VERBOSE:-0}"
log_file="$(mktemp)"
say() { if [[ "$verbose" == 1 ]]; then echo "== $*" >&2; fi; }
die() { echo "create-org: $*" >&2; exit 1; }
quiet() { if [[ "$verbose" == 1 ]]; then "$@"; else "$@" >>"$log_file" 2>&1; fi; }
on_error() {
  echo "create-org failed. Last output saved in $log_file." >&2
  echo "Start over with: deploy/scripts/remove-org.sh --force $slug" >&2
}

# ---- inputs ----------------------------------------------------------------------
[[ "$slug" =~ ^[a-z][a-z0-9-]{0,19}$ && "$slug" != *- ]] || die "slug must be 1 to 20 characters: lower case letters, digits, hyphens, starting with a letter."
case "$slug" in www|mail|edge|api|admin|root|default) die "slug '$slug' is reserved." ;; esac
[[ "$admin_email" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]] || die "'$admin_email' is not a valid email address."

if [[ -f "$ORGS_DIR/defaults.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$ORGS_DIR/defaults.env"
  set +a
fi
mkdir -p "$ORGS_DIR"
env_file="$ORGS_DIR/$slug.env"
if [[ -n "$(org_line "$slug")" || -e "$env_file" ]]; then die "organisation '$slug' already exists ($env_file or an entry in $ORGS_LIST)."; fi

domain="${DOMAIN:-}"
if [[ -z "$domain" && -f "$DEPLOY_DIR/.env" ]]; then
  domain="$(grep -E '^DOMAIN=' "$DEPLOY_DIR/.env" | tail -n1 | cut -d= -f2- | tr -d '\r ')"
fi
[[ -n "$domain" ]] || die "DOMAIN is not set. Export DOMAIN=<your domain> or put it in deploy/orgs/defaults.env."
[[ "$domain" =~ ^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$ ]] || die "DOMAIN '$domain' is not a plain lower case host name."

first=0
if [[ -z "$(org_lines)" ]]; then first=1; fi
if (( first )) && [[ -n "$(docker ps -aq --filter label=com.docker.compose.project=simguard 2>/dev/null | head -n1)" ]]; then
  die "a stack named 'simguard' already runs here but is not registered. Adopt it first (deploy/scripts/adopt-org.sh, docs/deployment.md 'Moving the live stack')."
fi
if (( first )); then host="$domain"; else host="$slug.$domain"; fi

# Every organisation has its own first Admin.
for f in "$ORGS_DIR"/*.env "$DEPLOY_DIR/.env"; do
  [[ -f "$f" ]] || continue
  if grep -qiF "SIMGUARD_ADMIN_EMAIL=$admin_email" "$f"; then
    die "$admin_email is already the first Admin of another organisation ($(basename "$f"))."
  fi
done

command -v openssl >/dev/null || die "openssl is required."
docker info >/dev/null 2>&1 || die "docker is not usable by this user."
project="${SIMGUARD_PROJECT_PREFIX:-simguard}-$slug"
prefix="${SIMGUARD_IMAGE_PREFIX:-simguard}"
root="${SIMGUARD_ORGS_ROOT:-/srv/simguard}"
org_root="$root/$slug"
trap on_error ERR

# ---- folders and data ------------------------------------------------------------
say "folders in $org_root"
mock_src="${SIMGUARD_MOCK_SOURCE:-$root/mock-data}"
[[ -d "$mock_src" ]] || mock_src="$repo/deploy/data-pack/mock-data"
art_src="${SIMGUARD_ML_ARTIFACTS_SOURCE:-$root/ml-artifacts}"
[[ -d "$art_src" ]] || art_src="$repo/ml-service/artifacts"
for f in sims.csv usage.csv sim_mapping.csv; do
  [[ -f "$mock_src/$f" ]] || die "mock data source $mock_src has no $f (set SIMGUARD_MOCK_SOURCE)."
done
[[ -f "$art_src/isolation_forest.joblib" ]] || die "model artifacts source $art_src looks wrong (set SIMGUARD_ML_ARTIFACTS_SOURCE)."

mkdir -p "$org_root/mock-data" "$org_root/ml-artifacts"
# Hard links when both folders share a disk (the data is only ever read), else a copy.
cp -al "$mock_src/." "$org_root/mock-data/" 2>/dev/null || cp -a "$mock_src/." "$org_root/mock-data/"
cp -a "$art_src/." "$org_root/ml-artifacts/"
chmod -R a+rX "$org_root/mock-data" "$org_root/ml-artifacts"

# The carrier keys of this organisation only: the key its backend uses to call ITS mock.
umask 077
{
  for carrier in Safaricom Airtel Telkom; do
    echo "CarrierMock__Keys__${carrier}=$(openssl rand -hex 24)"
  done
  echo "CarrierMock__DataDir=/data/mock-data"
  echo "CarrierMock__SessionsFile=/data/mock-data/synthetic_cdr_sessions.csv"
} > "$org_root/mock.env"
chmod 600 "$org_root/mock.env"

# ---- a free subnet for this stack's private network -----------------------------
docker_subnets="$(docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null || true)"
env_subnets="$(cat "$ORGS_DIR"/*.env "$DEPLOY_DIR/.env" 2>/dev/null | grep -E '^SIMGUARD_SUBNET=' | cut -d= -f2 || true)"
used="$(printf '%s %s' "$docker_subnets" "$env_subnets" | tr -d '\r' | tr '\n' ' ')"
subnet=""
for n in $(seq 78 250); do
  if [[ " $used " != *" 172.29.$n.0/24 "* ]]; then subnet="172.29.$n.0/24"; break; fi
done
[[ -n "$subnet" ]] || die "no free 172.29.x.0/24 subnet found."

# ---- env file (secrets generated, never printed) ---------------------------------
say "writing $env_file"
hex() { openssl rand -hex "$1"; }
{
  echo "# SimGuard organisation '$slug'. Written by deploy/scripts/create-org.sh. Holds secrets:"
  echo "# keep mode 600, never commit. Remove SIMGUARD_ADMIN_PASSWORD after the first sign-in."
  echo "COMPOSE_PROJECT_NAME=$project"
  echo "SIMGUARD_IMAGE_PREFIX=$prefix"
  if [[ -n "${SIMGUARD_EDGE_NETWORK:-}" ]]; then echo "SIMGUARD_EDGE_NETWORK=$SIMGUARD_EDGE_NETWORK"; fi
  echo "SIMGUARD_ORG_SLUG=$slug"
  echo "SIMGUARD_ORG_HOST=$host"
  echo "DOMAIN=$domain"
  echo "SIMGUARD_DB_NAME=simguard"
  echo "SIMGUARD_DB_OWNER=simguard_owner"
  echo "SIMGUARD_DB_OWNER_PASSWORD=$(hex 24)"
  echo "SIMGUARD_ML_USER=simguard_ml"
  echo "SIMGUARD_ML_PASSWORD=$(hex 24)"
  echo "JWT_KEY=$(hex 32)"
  echo "ML_SHARED_SECRET=$(hex 32)"
  echo "SIMGUARD_ML_ARTIFACTS_DIR=$(native "$org_root/ml-artifacts")"
  echo "SIMGUARD_MOCK_DATA_DIR=$(native "$org_root/mock-data")"
  echo "SIMGUARD_MOCK_ENV_FILE=$(native "$org_root/mock.env")"
  echo "SIMGUARD_ADMIN_EMAIL=$admin_email"
  echo "SIMGUARD_ADMIN_PASSWORD=$(hex 16)"
  echo "SIMGUARD_RECOVER_ADMIN=false"
  echo "BREVO_API_KEY=${BREVO_API_KEY:-}"
  if [[ -n "${BREVO_SENDER_EMAIL:-}" ]]; then echo "BREVO_SENDER_EMAIL=$BREVO_SENDER_EMAIL"; fi
  echo "BREVO_SIGN_IN_URL=https://$host/login"
  echo "CARRIER_FEED_ALLOW_INSECURE_HTTP=true"
  echo "CARRIER_MOCK_DATE=last"
  echo "SIMGUARD_SUBNET=$subnet"
  echo "ML_PROPHET_N_JOBS=1"
} > "$env_file"
chmod 600 "$env_file"

# ---- start the stack, wait for it, then publish it through Caddy ---------------
export SIMGUARD_ENV_FILE="$env_file" COMPOSE_PROJECT_NAME="$project"
unset SIMGUARD_ORG
lib_setup
ensure_edge_network

missing_image=0
for s in backend ml carriermock frontend; do
  docker image inspect "$prefix-$s:latest" >/dev/null 2>&1 || missing_image=1
done
if (( missing_image )); then
  for s in backend ml carriermock frontend; do
    say "building $s"
    quiet dc build "$s"
  done
fi
say "starting $project"
quiet dc up -d

services=(postgres backend ml carriermock frontend)
waiting=()
for _ in $(seq 1 72); do
  waiting=()
  for s in "${services[@]}"; do
    [[ "$(dc ps --format '{{.Health}}' "$s" 2>/dev/null | head -n1)" == healthy ]] || waiting+=("$s")
  done
  if (( ${#waiting[@]} == 0 )); then break; fi
  sleep 5
done
(( ${#waiting[@]} == 0 )) || die "not healthy after 6 minutes: ${waiting[*]} (docker compose -p $project logs <service>)"

printf '%s %s %s %s\n' "$slug" "$project" "$(native "$env_file")" "$host" >> "$ORGS_LIST"
say "adding the Caddy entry"
quiet "$here/edge.sh" ensure
trap - ERR
rm -f "$log_file"

echo "https://$host"
echo "First Admin $admin_email: the password is SIMGUARD_ADMIN_PASSWORD in $env_file (owner only; remove it after the first sign-in)."
