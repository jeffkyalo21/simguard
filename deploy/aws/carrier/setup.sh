#!/usr/bin/env bash
# One-time setup of the carrier server (Ubuntu 24.04). Run as root through
# Systems Manager Session Manager (no SSH), from any folder:
#   curl -fsSL https://raw.githubusercontent.com/jeffkyalo21/simguard/main/deploy/aws/carrier/setup.sh -o /tmp/setup.sh
#   sudo bash /tmp/setup.sh
# Safe to run again: every step checks first.
#
# It installs Docker (with the compose plugin), git, git-lfs and python3,
# clones the repository read-only to /opt/simguard-carrier (public, https, no
# token), and creates /srv/carrier/mock-data and /srv/carrier/state.
# It does NOT create mock.env, build the data or start anything (docs/deploy-checklist.md).
#
# Swap: measured (docs/carrier-server.md) the mock needs about 120 MiB running
# and about 200 MiB to build its image, so a t3.micro (1 GiB) needs no swap. A
# 1 GB swap file is added only on a smaller machine (under 768 MiB, a t3.nano).
set -euo pipefail

REPO_URL="${CARRIER_REPO_URL:-https://github.com/jeffkyalo21/simguard.git}"
REPO_DIR="${CARRIER_REPO_DIR:-/opt/simguard-carrier}"
SRV_DIR="/srv/carrier"
# The .NET images run as this user ($APP_UID); it writes the pointer file.
APP_UID=1654

if [[ $EUID -ne 0 ]]; then echo "Run as root: sudo bash $0" >&2; exit 2; fi
export DEBIAN_FRONTEND=noninteractive

echo "== packages"
apt-get update -q
apt-get install -y -q ca-certificates curl git git-lfs python3 openssl >/dev/null

echo "== docker"
if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL https://get.docker.com | sh >/dev/null
fi
systemctl enable --now docker >/dev/null 2>&1
docker compose version

echo "== swap"
mem_mib=$(( $(awk '/^MemTotal:/ {print $2}' /proc/meminfo) / 1024 ))
if (( mem_mib < 768 )); then
  if swapon --show=NAME --noheadings | grep -q .; then
    echo "swap already on: $(swapon --show=NAME,SIZE --noheadings | tr '\n' ' ')"
  else
    fallocate -l 1G /swapfile
    chmod 600 /swapfile
    mkswap /swapfile >/dev/null
    swapon /swapfile
    grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
    echo "added a 1 GB swap file (${mem_mib} MiB RAM)"
  fi
else
  echo "no swap needed (${mem_mib} MiB RAM)"
fi

echo "== folders"
mkdir -p "$SRV_DIR/mock-data" "$SRV_DIR/state"
chmod 755 "$SRV_DIR" "$SRV_DIR/mock-data"
chown "$APP_UID:$APP_UID" "$SRV_DIR/state"
chmod 750 "$SRV_DIR/state"

echo "== repository"
if [[ ! -d "$REPO_DIR/.git" ]]; then
  # LFS files are not downloaded on checkout; build-mock-data.sh pulls the one it needs.
  GIT_LFS_SKIP_SMUDGE=1 git clone --quiet --branch main "$REPO_URL" "$REPO_DIR"
fi
git -C "$REPO_DIR" lfs install --local --skip-smudge >/dev/null
echo "clone at $REPO_DIR, commit $(git -C "$REPO_DIR" rev-parse --short HEAD)"

echo
echo "Setup finished. Next (docs/deploy-checklist.md):"
echo "  sudo $REPO_DIR/deploy/carrier/scripts/build-mock-data.sh"
echo "  sudo $REPO_DIR/deploy/carrier/scripts/create-mock-env.sh"
echo "then run the deploy-carrier workflow once."
