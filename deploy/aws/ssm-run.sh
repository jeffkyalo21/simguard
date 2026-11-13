#!/usr/bin/env bash
# Runs a local script on one EC2 instance through AWS Systems Manager Run
# Command (document AWS-RunShellScript, as root) and waits for it. No SSH.
# Used by the deploy workflows; needs AWS credentials (OIDC role), the aws CLI and jq.
#
#   deploy/aws/ssm-run.sh <instance id> <script> [argument ...]
#
# The script travels inside the command (gzip, base64), so the instance needs
# no copy of this repository for it. Arguments must be plain words (no secrets:
# Run Command keeps the command text in its history). The script's output is
# printed when it ends (Systems Manager returns at most the last 24000
# characters of each stream). Exit status: the script's, or 1 on a timeout.
set -euo pipefail

instance="${1:?instance id}"
script="${2:?script path}"
shift 2
timeout_seconds="${SSM_EXECUTION_TIMEOUT:-3600}"

for a in "$@"; do
  if [[ ! "$a" =~ ^[A-Za-z0-9._:/=-]+$ ]]; then
    echo "Argument '$a' has characters other than letters, digits and . _ : / = -" >&2
    exit 2
  fi
done

payload="$(gzip -9c "$script" | base64 -w0)"
# Far below any Run Command parameter limit (the exact limit was not confirmed in
# the AWS docs; a third-party source names 100 KB). Keep the remote scripts small.
if (( ${#payload} > 48000 )); then
  echo "$script is too large to send inline (${#payload} characters after gzip and base64)." >&2
  exit 2
fi
remote="/tmp/ssm-$(basename "$script" .sh)-$$.sh"
params="$(jq -n \
  --arg payload "$payload" --arg remote "$remote" --arg args "$*" --arg timeout "$timeout_seconds" \
  '{commands: [
      "set -e",
      ("echo " + $payload + " | base64 -d | gunzip > " + $remote),
      ("trap \"rm -f " + $remote + "\" EXIT"),
      ("bash " + $remote + " " + $args)
    ],
    executionTimeout: [$timeout]}')"

command_id="$(aws ssm send-command \
  --instance-ids "$instance" \
  --document-name AWS-RunShellScript \
  --comment "${SSM_COMMENT:-deploy from GitHub Actions}" \
  --parameters "$params" \
  --timeout-seconds 600 \
  --query Command.CommandId --output text)"
echo "Run Command $command_id on $instance: $(basename "$script") $*"

deadline=$(( $(date +%s) + timeout_seconds + 120 ))
status=Pending
while (( $(date +%s) < deadline )); do
  sleep 10
  # Right after send-command the invocation may not exist yet.
  status="$(aws ssm get-command-invocation --command-id "$command_id" --instance-id "$instance" \
    --query Status --output text 2>/dev/null || echo Pending)"
  case "$status" in
    Pending|InProgress|Delayed) continue ;;
    *) break ;;
  esac
done

echo "----- output from the instance -----"
aws ssm get-command-invocation --command-id "$command_id" --instance-id "$instance" \
  --query StandardOutputContent --output text || true
err="$(aws ssm get-command-invocation --command-id "$command_id" --instance-id "$instance" \
  --query StandardErrorContent --output text 2>/dev/null || true)"
if [[ -n "$err" && "$err" != None ]]; then
  echo "----- errors from the instance -----"
  echo "$err"
fi
echo "------------------------------------"

case "$status" in
  Success) echo "Run Command finished: Success"; exit 0 ;;
  Pending|InProgress|Delayed) echo "::error::Run Command $command_id did not finish in time (last status $status)."; exit 1 ;;
  *) echo "::error::Run Command $command_id ended with status $status."; exit 1 ;;
esac
