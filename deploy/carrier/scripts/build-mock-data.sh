#!/usr/bin/env bash
# Builds the mock data folder on the carrier host from the repository clone:
# the "main" slice of the demo data pack (SIMs 0 to 699: 700 SIMs, 63000 usage
# rows, 90 days, without the large per-session file), the slice the SimGuard
# organisation imported. Nothing is copied from another machine.
#   sudo deploy/carrier/scripts/build-mock-data.sh             # into /srv/carrier/mock-data
#   sudo deploy/carrier/scripts/build-mock-data.sh --replace   # rebuild an existing folder
# Needs git-lfs and python3 (deploy/aws/carrier/setup.sh installs them). Downloads
# one 12 MB file from Git LFS. Writes no keys: mock.env is created separately.
set -euo pipefail
repo="${CARRIER_REPO_DIR:-/opt/simguard-carrier}"
target="${CARRIER_DATA_DIR:-/srv/carrier/mock-data}"
offset="${SIM_OFFSET:-0}"
count="${SIM_COUNT:-700}"
replace=0
for arg in "$@"; do
  case "$arg" in
    --replace) replace=1 ;;
    *) echo "unknown option: $arg (use --replace)" >&2; exit 2 ;;
  esac
done

if [[ $EUID -ne 0 ]]; then echo "Run with sudo." >&2; exit 2; fi
if [[ -f "$target/usage.csv" && $replace -eq 0 ]]; then
  echo "$target already has data; nothing changed. Use --replace to rebuild it." >&2
  exit 1
fi

git -C "$repo" lfs pull --include="ml-service/data/synthetic_cdr_daily_agg.csv,ml-service/data/synthetic_cdr_labels.csv"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
# An empty mock.env in the output folder stops prepare-data.py from generating
# keys (it never overwrites an existing mock.env). It is thrown away below.
: > "$work/mock.env"
python3 "$repo/deploy/scripts/prepare-data.py" --org-name main --out "$work" \
  --sim-offset "$offset" --sim-count "$count" --no-sessions >/dev/null

mkdir -p "$target"
for f in sims.csv usage.csv sim_mapping.csv; do
  install -m 644 "$work/mock-data/$f" "$target/$f"
done
chmod 755 "$target"
sims=$(( $(wc -l < "$target/sims.csv") - 1 ))
rows=$(( $(wc -l < "$target/usage.csv") - 1 ))
days="$(tail -n +2 "$target/usage.csv" | cut -d, -f2 | sort -u | wc -l)"
echo "Mock data in $target: $sims SIMs, $rows usage rows, $days days."
