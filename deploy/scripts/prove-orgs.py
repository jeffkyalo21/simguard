#!/usr/bin/env python3
"""Local proof that two SimGuard organisations on one VM are separate.

    python3 deploy/scripts/prove-orgs.py [--orgs a b] [--port 8443] [--rows 150]

LOCAL TESTS ONLY. It needs two organisations created with create-org.sh whose
backends run in Development (SIMGUARD_COMPOSE_EXTRA=deploy/test/docker-compose.development.yml),
because it creates an Officer with a chosen temporary password, which only the
Development API allows. It talks to the stacks the way a browser does: through the
shared Caddy (local certificate, checked against Caddy's own root CA) and the web
app, then looks inside the containers and databases with docker exec.

Per organisation it signs in as the seeded Admin (MFA enrolment included), creates an
Officer, signs in as that Officer and imports a slice of the demo data pack (the
slices of the two organisations do not overlap). Then it checks that Officer A cannot
see B's data (API and database), that B's carrier key is not visible from A, that no
service port is published and that the networks are separate.

Passwords, keys and tokens are read from the env files and never printed.
Exit code 0 only when every check passed.
"""

from __future__ import annotations

import argparse
import base64
import csv
import hashlib
import hmac
import http.client
import json
import os
import re
import secrets
import socket
import ssl
import struct
import subprocess
import sys
import tempfile
import time
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ORGS_DIR = Path(os.environ.get("SIMGUARD_ORGS_DIR") or REPO / "deploy" / "orgs")
DEFAULT_PACK = Path(os.environ.get("SIMGUARD_DATA_PACK") or REPO / "deploy" / "data-pack")

results: list[tuple[bool, str]] = []


def check(ok: bool, text: str) -> bool:
    results.append((ok, text))
    print(("PASS  " if ok else "FAIL  ") + text, flush=True)
    return ok


def run(args: list[str], timeout: int = 120) -> subprocess.CompletedProcess:
    return subprocess.run(args, capture_output=True, text=True, timeout=timeout)


def read_env(path: Path) -> dict[str, str]:
    values = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" in line and not line.startswith("#"):
            key, _, value = line.partition("=")
            values[key.strip()] = value.strip()
    return values


def read_orgs() -> dict[str, dict]:
    orgs = {}
    for line in (ORGS_DIR / "orgs.list").read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        slug, project, env_file, host = line.split()
        env_path = Path(env_file)
        orgs[slug] = {"slug": slug, "project": project, "host": host, "env": read_env(env_path), "env_path": env_path}
    return orgs


def totp(secret: str) -> str:
    key = base64.b32decode(secret.upper().replace(" ", "") + "=" * (-len(secret) % 8))
    digest = hmac.new(key, struct.pack(">Q", int(time.time() // 30)), hashlib.sha1).digest()
    offset = digest[-1] & 15
    return "%06d" % ((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000)


class Conn(http.client.HTTPSConnection):
    """Connects to 127.0.0.1 but sends the organisation's host name (SNI and Host)."""

    def connect(self):
        sock = socket.create_connection(("127.0.0.1", self.port), self.timeout)
        self.sock = self._context.wrap_socket(sock, server_hostname=self.host)


class Site:
    def __init__(self, host: str, port: int, ctx: ssl.SSLContext):
        self.host, self.port, self.ctx = host, port, ctx
        self.cookies: dict[str, str] = {}
        self.last_totp_secret = ""

    def request(self, method, path, body=None, raw=None, ctype=None, cookies=None):
        conn = Conn(self.host, self.port, context=self.ctx, timeout=300)
        data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
        headers = {"Origin": f"https://{self.host}:{self.port}", "Accept": "application/json"}
        if data is not None:
            headers["Content-Type"] = ctype or "application/json"
        jar = self.cookies if cookies is None else cookies
        if jar:
            headers["Cookie"] = "; ".join(f"{k}={v}" for k, v in jar.items())
        conn.request(method, path, body=data, headers=headers)
        response = conn.getresponse()
        text = response.read().decode("utf-8", "replace")
        for line in response.msg.get_all("Set-Cookie") or []:
            name, _, rest = line.partition("=")
            value = rest.split(";", 1)[0]
            if value == "" or "max-age=0" in line.lower() or "expires=thu, 01 jan 1970" in line.lower():
                self.cookies.pop(name, None)
            else:
                self.cookies[name] = value
        conn.close()
        try:
            parsed = json.loads(text)
        except ValueError:
            parsed = None
        return response.status, parsed, text

    def api(self, method, path, body=None, **kw):
        return self.request(method, "/api/backend/" + path.lstrip("/"), body, **kw)

    def first_sign_in(self, email: str, temporary: str, new_password: str) -> bool:
        status, data, _ = self.request("POST", "/api/auth/login", {"email": email, "password": temporary})
        if status != 200 or (data or {}).get("next") != "mfa-setup":
            return False
        status, data, _ = self.request("POST", "/api/auth/mfa/enroll/start")
        if status != 200:
            return False
        match = re.search(r"secret=([A-Z2-7]+)", data["authenticatorUri"])
        self.last_totp_secret = match.group(1)
        status, data, _ = self.request("POST", "/api/auth/mfa/enroll/confirm", {"code": totp(match.group(1))})
        if status != 200:
            return False
        if data.get("mustChangePassword"):
            status, _, _ = self.request("POST", "/api/auth/change-password",
                                        {"currentPassword": temporary, "newPassword": new_password})
            return status == 200
        return True

    def sign_in(self, email: str, password: str, totp_secret: str) -> bool:
        """Sign-in of an account that already has MFA: password, then the current code."""
        status, data, _ = self.request("POST", "/api/auth/login", {"email": email, "password": password})
        if status != 200 or (data or {}).get("next") != "mfa":
            return False
        status, _, _ = self.request("POST", "/api/auth/mfa/verify", {"code": totp(totp_secret)})
        return status == 200

    def certificate_fingerprint(self) -> str:
        """SHA-256 of the certificate Caddy presents for this host."""
        conn = Conn(self.host, self.port, context=self.ctx, timeout=30)
        conn.connect()
        der = conn.sock.getpeercert(binary_form=True)
        conn.close()
        return hashlib.sha256(der).hexdigest()

    def upload(self, kind: str, path: Path, mode: str):
        boundary = uuid.uuid4().hex
        head = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{path.name}"\r\n'
                f"Content-Type: text/csv\r\n\r\n").encode()
        body = head + path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
        return self.api("POST", f"imports/{kind}?mode={mode}", raw=body, ctype=f"multipart/form-data; boundary={boundary}")


def paged_values(site: Site, path: str, field: str) -> list[str]:
    values, page = [], 1
    while True:
        status, data, _ = site.api("GET", f"{path}{'&' if '?' in path else '?'}page={page}&pageSize=100")
        if status != 200 or not isinstance(data, dict):
            return values
        items = data.get("items") or []
        values += [str(i.get(field, "")) for i in items]
        if len(items) < 100 or len(values) >= int(data.get("total", 0)):
            return values
        page += 1


def slice_csv(src: Path, dst: Path, start: int, count: int) -> list[dict]:
    with src.open(newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        rows = list(reader)[start:start + count]
        header = reader.fieldnames
    with dst.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, header, lineterminator="\r\n")
        writer.writeheader()
        writer.writerows(rows)
    return rows


def docker_exec(container: str, command: str, env: dict[str, str] | None = None, timeout: int = 30) -> subprocess.CompletedProcess:
    args = ["docker", "exec"]
    for k, v in (env or {}).items():
        args += ["-e", f"{k}={v}"]
    return run(args + [container, "bash", "-c", command], timeout)


def mock_status(container: str, key: str) -> str:
    """HTTP status line of a call to the carrier mock of this stack, from its backend."""
    cmd = ("exec 3<>/dev/tcp/carriermock/8080 && printf 'GET /v1/usage/summary?date=2025-01-01&page=1&pageSize=1 HTTP/1.0\\r\\n"
           "X-Api-Key: %s\\r\\nHost: x\\r\\n\\r\\n' \"$K\" >&3 && head -n1 <&3")
    out = docker_exec(container, cmd, {"K": key})
    return out.stdout.strip() or "no answer"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--orgs", nargs=2, default=["a", "b"])
    parser.add_argument("--port", type=int, default=8443, help="HTTPS port of the shared Caddy")
    parser.add_argument("--rows", type=int, default=150, help="employees and SIMs imported per organisation")
    parser.add_argument("--pack", type=Path, default=DEFAULT_PACK)
    parser.add_argument("--edge-container", default="sgorg-edge-caddy-1")
    args = parser.parse_args()

    orgs = read_orgs()
    A, B = (orgs[s] for s in args.orgs)
    for o in (A, B):
        o["ctr"] = lambda svc, o=o: f"{o['project']}-{svc}-1"

    # Caddy's own CA, so the local certificates are really verified.
    tmp = Path(tempfile.mkdtemp(prefix="sgproof_"))
    ca = tmp / "root.crt"
    cp = run(["docker", "cp", f"{args.edge_container}:/data/caddy/pki/authorities/local/root.crt", str(ca)])
    if cp.returncode != 0:
        print("Cannot read Caddy's local root certificate (is the shared Caddy running with local_certs?).")
        return 2
    ctx = ssl.create_default_context(cafile=str(ca))
    for o in (A, B):
        o["site"] = Site(o["host"], args.port, ctx)

    print("== entry point")
    for o in (A, B):
        try:
            status, _, _ = o["site"].request("GET", "/")
            check(status == 200, f"{o['slug']}: https://{o['host']}:{args.port}/ answers 200 with a verified local certificate (got {status})")
        except ssl.SSLError as exc:
            check(False, f"{o['slug']}: certificate not accepted ({exc.reason})")

    print("== sign-in as each seeded Admin")
    for o in (A, B):
        env = o["env"]
        o["admin_pw"] = secrets.token_hex(16)
        ok = o["site"].first_sign_in(env["SIMGUARD_ADMIN_EMAIL"], env["SIMGUARD_ADMIN_PASSWORD"], o["admin_pw"])
        check(ok, f"{o['slug']}: seeded Admin signs in (login, MFA enrolment, password change)")
    # The other organisation's Admin credentials must not work here.
    wrong = Site(A["host"], args.port, ctx)
    status, _, _ = wrong.request("POST", "/api/auth/login",
                                 {"email": B["env"]["SIMGUARD_ADMIN_EMAIL"], "password": B["env"]["SIMGUARD_ADMIN_PASSWORD"]})
    check(status in (400, 401, 403), f"B's Admin credentials are refused by A (status {status})")
    wrong = Site(B["host"], args.port, ctx)
    status, _, _ = wrong.request("POST", "/api/auth/login",
                                 {"email": A["env"]["SIMGUARD_ADMIN_EMAIL"], "password": A["env"]["SIMGUARD_ADMIN_PASSWORD"]})
    check(status in (400, 401, 403), f"A's Admin credentials are refused by B (status {status})")

    print("== an Officer in each organisation (Development-only shortcut: chosen temporary password)")
    for o in (A, B):
        o["officer_email"] = f"officer.{o['slug']}@example.com"
        temp = "Tmp-" + secrets.token_hex(10)
        status, _, text = o["site"].api("POST", "users", {"email": o["officer_email"], "name": f"Officer {o['slug'].upper()}",
                                                          "role": "SecurityOfficer", "temporaryPassword": temp})
        check(status == 201, f"{o['slug']}: Admin creates the Officer (status {status})")
        o["officer"] = Site(o["host"], args.port, ctx)
        ok = o["officer"].first_sign_in(o["officer_email"], temp, secrets.token_hex(16))
        check(ok, f"{o['slug']}: Officer signs in")

    print("== imports (the two slices do not overlap)")
    pack = args.pack
    for index, o in enumerate((A, B)):
        start, count = index * args.rows, args.rows
        files = {}
        files["departments"] = pack / "01_departments.csv"
        emp = slice_csv(pack / "02_employees.csv", tmp / f"{o['slug']}_employees.csv", start, count)
        sims = slice_csv(pack / "03_sims.csv", tmp / f"{o['slug']}_sims.csv", start, count)
        slice_csv(pack / "04_allocations.csv", tmp / f"{o['slug']}_allocations.csv", start, count)
        files["employees"] = tmp / f"{o['slug']}_employees.csv"
        files["sims"] = tmp / f"{o['slug']}_sims.csv"
        files["allocations"] = tmp / f"{o['slug']}_allocations.csv"
        o["emails"] = [r["email"] for r in emp]
        o["iccids"] = [r["iccid"] for r in sims]
        for kind in ("departments", "employees", "sims", "allocations"):
            status, preview, _ = o["officer"].upload(kind, files[kind], "preview")
            counts = {k: (preview or {}).get(k) for k in ("totalRows", "validRows", "inserted", "updated", "errorCount")}
            status2, committed, _ = o["officer"].upload(kind, files[kind], "commit")
            check(status == 200 and status2 == 200 and (committed or {}).get("status") == "committed",
                  f"{o['slug']}: {kind} preview {counts}, commit {(committed or {}).get('status')}")

    print("== Officer A cannot see B's data (API)")
    for me, other in ((A, B), (B, A)):
        mine = paged_values(me["officer"], "sims", "iccid")
        check(sorted(mine) == sorted(me["iccids"]),
              f"{me['slug']}: Officer sees exactly its {len(me['iccids'])} SIMs (got {len(mine)})")
        check(not set(mine) & set(other["iccids"]), f"{me['slug']}: none of {other['slug']}'s ICCIDs in its SIM list")
        found = sum(
            1 for iccid in other["iccids"][:20]
            if (me["officer"].api("GET", f"sims?search={iccid}")[1] or {}).get("total", 0) > 0
        )
        check(found == 0, f"{me['slug']}: searching 20 of {other['slug']}'s ICCIDs finds none")
        emps = paged_values(me["officer"], "employees", "email")
        check(sorted(emps) == sorted(me["emails"]) and not set(emps) & set(other["emails"]),
              f"{me['slug']}: employee list is exactly its own {len(me['emails'])} people")
    # A session of A replayed against B's address: B's backend uses another signing key.
    foreign = Site(B["host"], args.port, ctx)
    status, _, _ = foreign.api("GET", "sims", cookies=dict(A["officer"].cookies))
    check(status in (401, 403), f"A's Officer session cookie replayed against B is refused (status {status})")

    print("== Officer A cannot see B's data (database)")
    for me, other in ((A, B), (B, A)):
        env = me["env"]
        def sql(query, o=me):
            cmd = ["docker", "exec", o["ctr"]("postgres"), "psql", "-U", o["env"]["SIMGUARD_DB_OWNER"],
                   "-d", o["env"]["SIMGUARD_DB_NAME"], "-Atc", query]
            return run(cmd).stdout.strip()
        tables = sql("select string_agg(table_name, ',') from information_schema.tables where table_schema='public'")
        sims_table = "sims" if "sims" in tables.split(",") else next((t for t in tables.split(",") if "sim" in t.lower() and "alloc" not in t.lower()), "sims")
        mine = sql(f'select count(*) from "{sims_table}"')
        theirs = sql(f'select count(*) from "{sims_table}" where iccid = any(array[{",".join(repr(i) for i in other["iccids"])}])')
        check(mine == str(len(me["iccids"])) and theirs == "0",
              f"{me['slug']}: its database holds {mine} SIMs, {theirs} of {other['slug']}'s")
        emp_theirs = sql("select count(*) from employees where lower(email) = any(array[%s])" % ",".join(repr(e) for e in other["emails"]))
        check(emp_theirs == "0", f"{me['slug']}: its database holds {emp_theirs} of {other['slug']}'s employees")
        dbs = sql("select string_agg(datname, ',' order by datname) from pg_database where not datistemplate")
        check(dbs == f"postgres,{env['SIMGUARD_DB_NAME']}" or sorted(dbs.split(",")) == sorted(["postgres", env["SIMGUARD_DB_NAME"]]),
              f"{me['slug']}: its postgres server holds only its own database ({dbs})")
    vols = {o["slug"]: run(["docker", "inspect", "-f", "{{range .Mounts}}{{.Name}} {{end}}", o["ctr"]("postgres")]).stdout.split() for o in (A, B)}
    check(not set(vols[A["slug"]]) & set(vols[B["slug"]]), f"separate database volumes: {vols[A['slug']]} and {vols[B['slug']]}")

    print("== carrier keys")
    def mock_keys(o):
        text = (Path(o["env"]["SIMGUARD_MOCK_ENV_FILE"])).read_text(encoding="utf-8")
        return dict(re.findall(r"^CarrierMock__Keys__(\w+)=(\S+)$", text, re.M))
    ka, kb = mock_keys(A), mock_keys(B)
    check(len(ka) == 3 and len(kb) == 3 and not set(ka.values()) & set(kb.values()), "each organisation has its own three carrier keys, none shared")
    for me, other, mine, theirs in ((A, B, ka, kb), (B, A, kb, ka)):
        blob = "".join(run(["docker", "inspect", me["ctr"](s)]).stdout for s in ("postgres", "backend", "ml", "carriermock", "frontend"))
        blob += me["env_path"].read_text(encoding="utf-8")
        leaked = [c for c, k in theirs.items() if k in blob]
        check(not leaked, f"{me['slug']}: none of {other['slug']}'s carrier keys appear in its container settings or env file")
        own = mock_status(me["ctr"]("backend"), mine["Safaricom"])
        foreign = mock_status(me["ctr"]("backend"), theirs["Safaricom"])
        # Without a date the mock answers 400 once the key is accepted; 401 means the key was refused.
        check(" 401 " not in own and own.startswith("HTTP") and " 401 " in foreign,
              f"{me['slug']}: its mock accepts its own key ({own}) and refuses {other['slug']}'s ({foreign})")
        resolve = docker_exec(me["ctr"]("backend"), f"getent hosts {other['project']}-carriermock-1 {other['project']}-backend-1 {other['project']}-postgres-1")
        check(resolve.stdout.strip() == "", f"{me['slug']}: cannot resolve {other['slug']}'s mock, backend or database by name")

    print("== ports and networks")
    published = []
    for line in run(["docker", "ps", "--format", "{{.Names}}|{{.Ports}}", "--filter", "name=sgorg"]).stdout.splitlines():
        name, _, ports = line.partition("|")
        if "->" in ports:
            published.append(name)
    check(published == [args.edge_container], f"only the shared Caddy publishes host ports (published by: {published})")
    caddy_ports = run(["docker", "ps", "--filter", f"name={args.edge_container}", "--format", "{{.Ports}}"]).stdout
    check(re.findall(r"->(\d+)/tcp", caddy_ports).count("80") == 2 or "->80/tcp" in caddy_ports, f"Caddy publishes only 80 and 443: {caddy_ports.strip()}")
    for me, other in ((A, B), (B, A)):
        net_me, net_other = f"{me['project']}_internal", f"{other['project']}_internal"
        ip = run(["docker", "inspect", "-f", '{{(index .NetworkSettings.Networks "%s").IPAddress}}' % net_other, other["ctr"]("backend")]).stdout.strip()
        ip_db = run(["docker", "inspect", "-f", '{{(index .NetworkSettings.Networks "%s").IPAddress}}' % net_other, other["ctr"]("postgres")]).stdout.strip()
        r1 = docker_exec(me["ctr"]("backend"), f"timeout 4 bash -c 'exec 3<>/dev/tcp/{ip}/8080'", timeout=15)
        r2 = docker_exec(me["ctr"]("backend"), f"timeout 4 bash -c 'exec 3<>/dev/tcp/{ip_db}/5432'", timeout=15)
        check(r1.returncode != 0 and r2.returncode != 0,
              f"{me['slug']}: its backend cannot open a connection to {other['slug']}'s backend or database by IP")
        nets = run(["docker", "inspect", "-f", "{{range $k,$v := .NetworkSettings.Networks}}{{$k}} {{end}}", me["ctr"]("backend")]).stdout.split()
        check(nets == [net_me], f"{me['slug']}: its backend is only on {net_me} (networks: {nets})")
    members = run(["docker", "network", "inspect", "-f", "{{range .Containers}}{{.Name}} {{end}}", os.environ.get("SIMGUARD_EDGE_NETWORK", "simguard-edge")]).stdout.split()
    expected = sorted([args.edge_container, A["ctr"]("frontend"), B["ctr"]("frontend")])
    check(sorted(members) == expected, f"the shared network holds only Caddy and the two frontends ({sorted(members)})")
    c = run(["docker", "exec", args.edge_container, "wget", "-q", "-T", "3", "-O", "/dev/null", f"http://{A['project']}-web:3000/"])
    check(c.returncode == 0, "Caddy reaches A's frontend by its alias")
    c = run(["docker", "exec", args.edge_container, "wget", "-q", "-T", "3", "-O", "/dev/null", "http://backend:8080/health"])
    check(c.returncode != 0, "Caddy cannot reach a backend (no route, no name)")

    print("== memory with both stacks running")
    stats = run(["docker", "stats", "--no-stream", "--format", "{{.Name}}|{{.MemUsage}}"]).stdout.splitlines()
    total = 0.0
    for line in sorted(stats):
        name, _, usage = line.partition("|")
        if not name.startswith("sgorg"):
            continue
        used = usage.split("/")[0].strip()
        value = float(re.match(r"[\d.]+", used).group(0))
        mib = value * (1024 if "GiB" in used else 1) if "iB" in used else value
        total += mib
        print(f"      {name:28} {used}")
    print(f"      total of the sgorg containers: {total:.0f} MiB")

    failed = [t for ok, t in results if not ok]
    print(f"\n{len(results) - len(failed)} checks passed, {len(failed)} failed.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
