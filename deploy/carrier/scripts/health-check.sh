#!/usr/bin/env bash
# Checks the mock on loopback and Caddy with its certificate on this host.
# (From this host the public address is blocked by the security group, which
# opens 443 to the SimGuard server only, so Caddy is reached with --resolve.)
#   deploy/carrier/scripts/health-check.sh
set -euo pipefail
domain="${CARRIER_DOMAIN:-carrier.sim-guard.app}"

mock="$(curl -sS --max-time 5 http://127.0.0.1:8081/ || true)"
if [[ "$mock" != *'"sims":'* ]]; then
  echo "FAIL  mock on 127.0.0.1:8081 does not answer"
  exit 1
fi
echo "OK    mock: $mock"

code="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' --resolve "$domain:443:127.0.0.1" "https://$domain/" || true)"
if [[ "$code" != 200 ]]; then
  echo "FAIL  https://$domain/ through Caddy answered '${code:-nothing}' (certificate not issued yet, or Caddy down: docker logs simguard-carrier-caddy-1)"
  exit 1
fi
echo "OK    https://$domain/ through Caddy with a valid certificate"
