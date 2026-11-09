#!/usr/bin/env bash
# Restores a dump from the backup volume into a NAMED database.
# Usage: deploy/scripts/restore.sh <dump file name> <target database> [--replace]
#   list dumps:  deploy/scripts/restore.sh --list
# The target is created when missing. If it already has tables, --replace is
# required and the tables are dropped and recreated from the dump. To restore
# over the live database, stop the backend first (docs/deployment.md).
# Another organisation than the first: SIMGUARD_ORG=<slug> deploy/scripts/restore.sh ...
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

if [[ "${1:-}" == "--list" ]]; then
  dc exec -T postgres sh -c "ls -1t /backups/*.dump 2>/dev/null | sed 's#.*/##'"
  exit 0
fi

dump="${1:-}"; target="${2:-}"; replace="${3:-}"
if [[ -z "$dump" || -z "$target" ]]; then
  echo "Usage: restore.sh <dump file name> <target database> [--replace] | --list" >&2
  exit 2
fi
if [[ ! "$target" =~ ^[A-Za-z0-9_]+$ ]]; then echo "Database name: letters, digits, underscore only." >&2; exit 2; fi
dump="$(basename "$dump")"
dc exec -T postgres test -f "/backups/$dump" || { echo "No such dump in the backup volume: $dump" >&2; exit 1; }

exists="$(psql_owner -d postgres -Atc "select 1 from pg_database where datname = '$target'")"
if [[ -z "$exists" ]]; then
  psql_owner -d postgres -qc "create database \"$target\""
else
  tables="$(psql_owner -d "$target" -Atc "select count(*) from information_schema.tables where table_schema = 'public'")"
  if [[ "$tables" != "0" && "$replace" != "--replace" ]]; then
    echo "Database $target already has $tables tables. Use --replace to overwrite it." >&2
    exit 1
  fi
fi

# --no-owner: objects belong to the user running the restore. --clean --if-exists
# makes --replace and a first restore the same command.
dc exec -T postgres pg_restore -U "$DB_OWNER" -d "$target" --no-owner --clean --if-exists --exit-on-error "/backups/$dump"
echo "Restored $dump into $target."
