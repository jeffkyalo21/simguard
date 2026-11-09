#!/usr/bin/env python3
"""Half of the proof that moving the live stack to the shared Caddy loses nothing.

    python3 deploy/scripts/prove-migration.py before --host H --port P --env deploy/.env --state FILE
    ... run the migration commands of docs/deployment.md ...
    python3 deploy/scripts/prove-migration.py after  --host H --port P --state FILE

LOCAL TESTS ONLY. `before` signs in as the first Admin of the existing stack (MFA
enrolment included), creates an Officer, signs in as the Officer and creates a carrier with a saved feed API key through the web app,
and saves what it needs (new password, authenticator key, certificate fingerprint) in
the state file (mode 600, outside the repository). `after` signs in again with the
saved password and key, reads the carrier back, tests the saved key again, and compares the
certificate fingerprint: the same accounts, the same database, the same Data
Protection keys and the same certificate must come out of the migration.
Secrets are never printed. Needs Caddy's local root certificate: --ca FILE
(docker cp <caddy container>:/data/caddy/pki/authorities/local/root.crt FILE).
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import secrets
import ssl
import sys
from pathlib import Path

spec = importlib.util.spec_from_file_location("prove_orgs", Path(__file__).with_name("prove-orgs.py"))
prove = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prove)

CARRIER = "Migration Check Carrier"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("phase", choices=["before", "after"])
    parser.add_argument("--host", required=True)
    parser.add_argument("--port", type=int, default=8443)
    parser.add_argument("--ca", type=Path, required=True)
    parser.add_argument("--state", type=Path, required=True)
    parser.add_argument("--env", type=Path, help="the stack's env file (phase before)")
    parser.add_argument("--mock-env", type=Path, help="the stack's mock.env, for its carrier key (phase before)")
    args = parser.parse_args()

    ctx = ssl.create_default_context(cafile=str(args.ca))
    site = prove.Site(args.host, args.port, ctx)

    if args.phase == "before":
        env = prove.read_env(args.env)
        new_password = secrets.token_hex(16)
        ok = site.first_sign_in(env["SIMGUARD_ADMIN_EMAIL"], env["SIMGUARD_ADMIN_PASSWORD"], new_password)
        prove.check(ok, "before: first Admin signs in (login, MFA enrolment, password change)")
        # Business data needs an Officer. A Production API cannot set the Officer's password
        # (the invitation email would), so this local test runs the backend in Development
        # (deploy/test/docker-compose.development.yml) before AND after the migration.
        officer = prove.Site(args.host, args.port, ctx)
        temp, officer_email = "Tmp-" + secrets.token_hex(10), "officer.migration@example.com"
        status, _, _ = site.api("POST", "users", {"email": officer_email, "name": "Officer Migration",
                                                  "role": "SecurityOfficer", "temporaryPassword": temp})
        prove.check(status == 201, f"before: Admin creates an Officer (status {status})")
        officer_password = secrets.token_hex(16)
        prove.check(officer.first_sign_in(officer_email, temp, officer_password), "before: the Officer signs in")
        site = officer
        # Data that must survive: a carrier whose feed API key is stored encrypted with the
        # Data Protection keys (volume dpkeys). The key is the mock's Safaricom key.
        mock_text = args.mock_env.read_text(encoding="utf-8")
        key = prove.re.search(r"^CarrierMock__Keys__Safaricom=(\S+)$", mock_text, prove.re.M).group(1)
        status, carrier, _ = site.api("POST", "carriers", {"name": CARRIER})
        prove.check(status == 201, f"before: Officer creates a carrier (status {status})")
        cid = (carrier or {}).get("id")
        status, _, _ = site.api("PUT", f"carriers/{cid}/feed", {"baseUrl": "http://carriermock:8080", "enabled": True, "apiKey": key})
        prove.check(status == 200, f"before: Officer saves the carrier feed with its API key (status {status})")
        status, _, _ = site.api("POST", f"carriers/{cid}/feed/test")
        prove.check(status == 200, f"before: the saved feed key works against the carrier mock (status {status})")
        state = {
            "email": officer_email, "password": officer_password, "totp": officer.last_totp_secret,
            "fingerprint": site.certificate_fingerprint(), "carrier": cid,
        }
        args.state.write_text(json.dumps(state), encoding="utf-8")
        os.chmod(args.state, 0o600)
    else:
        state = json.loads(args.state.read_text(encoding="utf-8"))
        ok = site.sign_in(state["email"], state["password"], state["totp"])
        prove.check(ok, "after: the same Officer signs in with the same password and authenticator key")
        status, data, _ = site.api("GET", "carriers")
        rows = data if isinstance(data, list) else (data or {}).get("items", [])
        mine = [c for c in rows if c.get("id") == state["carrier"]]
        prove.check(status == 200 and len(mine) == 1 and mine[0].get("feedKeySet") is True,
                    "after: the carrier created before the migration is still there with its feed key saved")
        status, _, _ = site.api("POST", f"carriers/{state['carrier']}/feed/test")
        prove.check(status == 200, f"after: the saved (encrypted) feed key still decrypts and works (status {status})")
        prove.check(site.certificate_fingerprint() == state["fingerprint"],
                    "after: Caddy presents the same certificate as before (certificate volume kept)")

    failed = [t for ok, t in prove.results if not ok]
    print(f"{len(prove.results) - len(failed)} checks passed, {len(failed)} failed.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
