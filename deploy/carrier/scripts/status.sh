#!/usr/bin/env bash
# Shows the release pointer: released days, the newest released date, the next
# nightly release (local time and UTC) and the days left.
#   sudo deploy/carrier/scripts/status.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
mock_admin GET /admin/status
