#!/usr/bin/env bash
# Sets the release pointer back to MOCK_START_DAY (default 80), or to the given
# day. SimGuard keeps the days it already imported; to pull them again lower the
# feed's start date in SimGuard Settings (rows are upserted, nothing is doubled).
#   sudo deploy/carrier/scripts/reset-pointer.sh          # back to the start day
#   sudo deploy/carrier/scripts/reset-pointer.sh 85       # days 1 to 85 released
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
if [[ $# -gt 1 ]] || [[ $# -eq 1 && ! "$1" =~ ^[0-9]+$ ]]; then
  echo "Usage: $0 [day]   (day: a whole number, 0 or more)" >&2
  exit 2
fi
if [[ $# -eq 1 ]]; then
  mock_admin POST /admin/reset-pointer "{\"day\": $1}"
else
  mock_admin POST /admin/reset-pointer '{}'
fi
