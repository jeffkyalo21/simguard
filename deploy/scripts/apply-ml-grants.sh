#!/usr/bin/env bash
# Creates or refreshes the read-only ML role. Run it after the migrations and
# after every update that adds a migration (see deploy/postgres/ml-readonly.sql).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ML_USER="$(cfg SIMGUARD_ML_USER)"
ML_PASSWORD="$(cfg SIMGUARD_ML_PASSWORD)"
if [[ -z "$ML_USER" || -z "$ML_PASSWORD" ]]; then
  echo "SIMGUARD_ML_USER and SIMGUARD_ML_PASSWORD must be set in deploy/.env" >&2
  exit 2
fi

# The view exists only after the migrations have run.
if [[ "$(psql_owner -d "$DB_NAME" -Atc "select to_regclass('public.ml_daily_usage') is not null")" != "t" ]]; then
  echo "View ml_daily_usage not found in $DB_NAME. Run the backend migrations first." >&2
  exit 1
fi

# The password reaches psql through the environment, not the command line.
dc exec -T -e ML_USER="$ML_USER" -e ML_PASSWORD="$ML_PASSWORD" postgres \
  psql -v ON_ERROR_STOP=1 -q -U "$DB_OWNER" -d "$DB_NAME" -f - < "$DEPLOY_DIR/postgres/ml-readonly.sql" > /dev/null
echo "ML role $ML_USER now has SELECT on ml_daily_usage only."
