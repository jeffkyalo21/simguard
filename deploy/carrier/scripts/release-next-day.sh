#!/usr/bin/env bash
# Releases one more day now (the same as the nightly release). The nightly
# schedule is not moved: tonight's release still happens.
# At the end of the data it answers "no more days" and changes nothing.
#   sudo deploy/carrier/scripts/release-next-day.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
mock_admin POST /admin/release-next-day
