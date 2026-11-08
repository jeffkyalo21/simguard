#!/usr/bin/env bash
# Restores the newest dump into a throwaway database, compares the row count of
# every table with the live database, then drops the throwaway database.
# Usage: deploy/scripts/test-restore.sh [--allow-growth]
#   --allow-growth  a live table with MORE rows than the dump is accepted
#                   (new data since the backup); fewer rows is always an error.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
here="$(dirname "${BASH_SOURCE[0]}")"
allow_growth="${1:-}"

latest="$(dc exec -T postgres sh -c "ls -1t /backups/${DB_NAME}_*.dump 2>/dev/null | head -n1 | sed 's#.*/##'" | tr -d '\r')"
if [[ -z "$latest" ]]; then echo "No dump found. Run backup.sh first." >&2; exit 1; fi

scratch="restore_test_$(date -u +%Y%m%d%H%M%S)"
cleanup() { psql_owner -d postgres -qc "drop database if exists \"$scratch\" with (force)" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "Restoring $latest into $scratch"
"$here/restore.sh" "$latest" "$scratch" >/dev/null

counts_sql="select table_name || ' ' || (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text
from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1"
live="$(psql_owner -d "$DB_NAME" -Atc "$counts_sql" | tr -d '\r')"
restored="$(psql_owner -d "$scratch" -Atc "$counts_sql" | tr -d '\r')"

status=0; tables=0
while read -r table liveCount; do
  [[ -z "$table" ]] && continue
  tables=$((tables + 1))
  restoredCount="$(printf '%s\n' "$restored" | awk -v t="$table" '$1 == t { print $2 }')"
  if [[ "$restoredCount" == "$liveCount" ]]; then
    printf '  ok       %-32s %s\n' "$table" "$liveCount"
  elif [[ "$allow_growth" == "--allow-growth" && -n "$restoredCount" && "$liveCount" -gt "$restoredCount" ]]; then
    printf '  grown    %-32s live %s, dump %s\n' "$table" "$liveCount" "$restoredCount"
  else
    printf '  MISMATCH %-32s live %s, dump %s\n' "$table" "$liveCount" "${restoredCount:-missing}"
    status=1
  fi
done <<< "$live"

if [[ $status -eq 0 ]]; then echo "Restore test passed: $tables tables match ($latest)."; else echo "Restore test FAILED ($latest)." >&2; fi
exit $status
