#!/usr/bin/env bash
# Local check of the deploy automation, before a push:
#   - actionlint on the two deploy workflows (it also runs shellcheck on their run: steps)
#   - bash -n and shellcheck on every deploy and carrier host script
# Uses the local actionlint and shellcheck when installed, otherwise their
# official Docker images (needs Docker). Changes nothing.
#   bash deploy/aws/check.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

workflows=(.github/workflows/deploy-carrier.yml .github/workflows/deploy-simguard.yml)
scripts=(deploy/aws/ssm-run.sh deploy/aws/check.sh deploy/aws/carrier/setup.sh
  deploy/aws/remote/deploy-carrier.sh deploy/aws/remote/deploy-simguard.sh
  deploy/carrier/scripts/*.sh)
failed=0

echo "== bash -n"
for f in "${scripts[@]}"; do
  bash -n "$f" || { echo "syntax error: $f"; failed=1; }
done
echo "${#scripts[@]} scripts parsed"

echo "== shellcheck"
if command -v shellcheck >/dev/null 2>&1; then
  shellcheck -x -S warning "${scripts[@]}" || failed=1
else
  MSYS_NO_PATHCONV=1 docker run --rm -v "$PWD:/mnt" -w /mnt koalaman/shellcheck:v0.10.0 -x -S warning "${scripts[@]}" || failed=1
fi

echo "== actionlint"
if command -v actionlint >/dev/null 2>&1; then
  actionlint "${workflows[@]}" || failed=1
else
  MSYS_NO_PATHCONV=1 docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:1.7.7 -no-color "${workflows[@]}" || failed=1
fi

if (( failed )); then echo "CHECK FAILED"; exit 1; fi
echo "all checks passed"
