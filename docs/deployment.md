# Deployment

Issues #22 to #26. One Ubuntu VM (4 GB RAM, 2 GB swap) runs the whole system
with one command: `docker compose -f deploy/docker-compose.yml up -d --build`.

| Service | What it is | Reachable from |
| ------- | ---------- | -------------- |
| `caddy` | the only public service: ports 80 and 443, certificates from Let's Encrypt | the internet |
| `frontend` | Next.js web app (port 3000); it calls the API on behalf of the browser | `caddy` |
| `backend` | .NET 10 API (port 8080); applies migrations at start | `frontend` |
| `ml` | Python scoring service (port 8001), read-only database role, shared secret | `backend` |
| `carriermock` | the demo carrier usage feed (port 8080), data in a mounted folder | `backend` |
| `postgres` | PostgreSQL 16 (port 5432), data in the volume `pgdata` | `backend`, `ml`, `dbgrants` |
| `dbgrants` | one-shot job: creates or refreshes the ML role after the backend has migrated | exits |

Only `caddy` publishes ports. Everything else lives on the private network
`internal` (subnet `172.29.77.0/24`, change with `SIMGUARD_SUBNET`). Start order
(health based): postgres, backend, dbgrants, ml; backend, frontend, caddy.
Every service has a health check and `restart: unless-stopped`.

## Fresh VM, step by step

Commands marked "laptop" run in a checkout of the repository on your own
computer, the rest on the VM. Replace `user@VM`, `<repo url>` and the domain.

### 1. Install Docker, swap and the firewall

```bash
sudo apt-get update && sudo apt-get install -y ca-certificates curl git openssl
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER"        # then log out and back in
docker compose version                 # must print a version

# 2 GB swap (skip if "swapon --show" already lists one). The image builds need it.
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw enable
```

### 2. DNS

Add an A record for your domain (default `sim-guard.app`) pointing at the VM's
public address. Wait until `getent hosts <domain>` on the VM prints that
address; Let's Encrypt needs it. `preflight.sh` checks this.

### 3. Clone

```bash
sudo mkdir -p /opt/simguard /srv/simguard && sudo chown "$USER" /opt/simguard /srv/simguard
git clone <repo url> /opt/simguard && cd /opt/simguard
```

### 4. Copy the model artifacts and the data pack

Nothing of this is baked into an image. Build the pack on your laptop and copy
it up (the artifacts are in the repository too; copy them from the checkout if
you prefer: `cp -r /opt/simguard/ml-service/artifacts /srv/simguard/ml-artifacts`).

```bash
# laptop, repository root
python3 deploy/scripts/prepare-data.py       # writes deploy/data-pack/ (about 115 MB with the sessions file)

scp -r ml-service/artifacts       user@VM:/srv/simguard/ml-artifacts
scp -r deploy/data-pack/mock-data user@VM:/srv/simguard/mock-data
scp    deploy/data-pack/mock.env  user@VM:/srv/simguard/mock.env
ssh user@VM 'chmod 600 /srv/simguard/mock.env && chmod -R a+rX /srv/simguard/ml-artifacts /srv/simguard/mock-data'
```

The five numbered CSV files in `deploy/data-pack/` stay on the laptop: you
upload them in the browser in step 9. `mock.env` holds the three carrier keys;
it is never printed or committed.

### 5. Fill in `deploy/.env`

```bash
cd /opt/simguard
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env                  # it holds passwords; only you may read it
for k in SIMGUARD_DB_OWNER_PASSWORD SIMGUARD_ML_PASSWORD; do sed -i "s|^$k=.*|$k=$(openssl rand -hex 24)|" deploy/.env; done
sed -i "s|^JWT_KEY=.*|JWT_KEY=$(openssl rand -hex 32)|; s|^ML_SHARED_SECRET=.*|ML_SHARED_SECRET=$(openssl rand -hex 32)|" deploy/.env
nano deploy/.env                       # set DOMAIN, SIMGUARD_ADMIN_EMAIL, SIMGUARD_ADMIN_PASSWORD (12+ characters)
```

`deploy/.env` is gitignored. Never put real values in `.env.example`. Variables:

| Variable | Meaning |
| -------- | ------- |
| `DOMAIN` | the one public hostname (default `sim-guard.app`) |
| `SIMGUARD_DB_NAME`, `SIMGUARD_DB_OWNER`, `SIMGUARD_DB_OWNER_PASSWORD` | database and the owner account the backend uses |
| `SIMGUARD_ML_USER`, `SIMGUARD_ML_PASSWORD` | the ML service's read-only role |
| `JWT_KEY` | signs sign-in tokens; 32 or more characters |
| `ML_SHARED_SECRET` | backend to ML service secret; 32 or more characters; differs from `JWT_KEY` |
| `SIMGUARD_ML_ARTIFACTS_DIR` | folder with the model artifacts (default `/srv/simguard/ml-artifacts`) |
| `SIMGUARD_MOCK_DATA_DIR`, `SIMGUARD_MOCK_ENV_FILE` | carrier mock data folder and its key file |
| `SIMGUARD_ADMIN_EMAIL`, `SIMGUARD_ADMIN_PASSWORD` | first Admin, used only while there are no accounts |
| `SIMGUARD_RECOVER_ADMIN` | `true` only for the emergency recovery below |
| `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `BREVO_SIGN_IN_URL` | email; see "Email" |
| `CARRIER_FEED_ALLOW_INSECURE_HTTP` | `true` for the demo mock over plain http; `false` with real carriers |
| `CARRIER_MOCK_DATE` | the mock's "today": `last` (default, the last day in the data) or `yyyy-MM-dd` |
| `SIMGUARD_SUBNET`, `ML_PROPHET_N_JOBS`, `COMPOSE_PROJECT_NAME` | optional |
| `HTTP_PORT`, `HTTPS_PORT`, `CADDY_GLOBAL_OPTIONS` | local tests only (see Decisions); leave unset on the VM |

Passwords use letters and digits (`openssl rand -hex`) because they go into a
connection string.

### 6. Preflight

```bash
deploy/scripts/preflight.sh
```

It stops with a plain message for each problem: `.env` missing or readable by
others, a variable missing or still `CHANGE_ME` or a placeholder, a secret that
is too short, the artifacts or mock folders missing or incomplete, the domain
not pointing at this machine, docker or memory. It prints no secret. Fix every
`FAIL` line and run it again until it says `Ready`.

### 7. Start

```bash
docker compose -f deploy/docker-compose.yml up -d --build
docker compose -f deploy/docker-compose.yml ps
```

The first build takes a while on a small VM (the web app and the API are
compiled on the VM; budget 20 to 30 minutes). Later starts take about 20
seconds. All services show `healthy`; `dbgrants` shows `Exited (0)`, which is
correct. Caddy fetches the certificate on first start:
`docker compose -f deploy/docker-compose.yml logs caddy | grep -i certificate`.

### 8. First sign-in

Open `https://<domain>`, sign in with `SIMGUARD_ADMIN_EMAIL` and
`SIMGUARD_ADMIN_PASSWORD`, scan the MFA QR code, keep the recovery codes and
choose a new password. Then take the seed password out and restart only the
backend:

```bash
sed -i 's|^SIMGUARD_ADMIN_PASSWORD=.*|SIMGUARD_ADMIN_PASSWORD=|' deploy/.env
docker compose -f deploy/docker-compose.yml up -d --no-deps backend
```

The Admin page, System health, shows API, Database and ML service as
`Working`. Email shows `Needs attention` until Brevo is set up (next section).
Scoring shows `Needs attention` until the first scoring run has finished.
Create a second Admin now (see "Admin accounts survive").

The ML role needs no manual step: `dbgrants` creates and refreshes it after the
backend has migrated, every time the stack is started. To do it by hand:
`deploy/scripts/apply-ml-grants.sh`.

### 9. Import the data, then connect the demo feed

Set up email first (below), then in the Admin screen invite a Security Officer
and let that person sign in. As the Security Officer open Imports and upload
the files from `deploy/data-pack/` on your laptop in the order of `README.txt`
(departments, employees, SIMs, allocations, usage): choose the type, preview,
then commit, and wait for each commit to finish. Expected result: 4 departments,
1000 employees, 1000 SIMs, 1000 allocations, 90000 usage rows. Committing the
usage file queues a scoring run (about 4 minutes); the Scoring tile turns
`Working` when it succeeds. "Refresh scores" starts another run on demand.

Carrier feed (demo): Settings, Carriers, Usage feed. Address
`http://carriermock:8080` (the Compose name of the mock), API key from the file
on the VM, one carrier at a time:

```bash
grep '^CarrierMock__Keys__Safaricom=' /srv/simguard/mock.env | cut -d= -f2     # type it in; do not paste it into chat
```

Use Test, then Sync now. The key is stored encrypted (Data Protection key ring
in the volume `dpkeys`), so it survives restarts and updates. If you ever
delete the volume `dpkeys`, saved carrier keys must be entered again.

### 10. Backups

See "Backup and restore" below. Add the cron line now.

## Email (Brevo must work before you invite users)

Outside Development an Admin cannot choose another person's password. An
invited user receives a generated password by email only; nobody can read it
afterwards. If email does not work, invited people cannot sign in. The first
Admin is not affected (its password comes from `.env`).

1. In Brevo, verify the sender domain (SPF and DKIM) for the address in
   `BREVO_SENDER_EMAIL`, then create an API key.
2. Put `BREVO_API_KEY` and `BREVO_SENDER_EMAIL` in `deploy/.env`. The link in the
   email is `https://<DOMAIN>/login` unless you set `BREVO_SIGN_IN_URL`.
3. `docker compose -f deploy/docker-compose.yml up -d --no-deps backend`.
4. System health, Email: `Working` means a key is set. It does not prove delivery.
   Prove it: invite yourself with a second address and check that the email arrives.
5. If nothing arrives: `docker compose -f deploy/docker-compose.yml logs backend | grep -i brevo`.
   `Brevo:ApiKey is not set` means the key did not reach the backend (restart it
   as in step 3). `Brevo refused an email with status 401` means a wrong key;
   `400` usually means the sender address is not verified.

## ML service is not reachable

Signs: System health, ML service, `Down`; "Refresh scores" produces a failed run
with a message such as `ML service at http://ml:8001 is unreachable`; no new
alerts. Everything else (sign-in, screens, imports) keeps working, and no data
is lost: when the service is back, press "Refresh scores" and the unscored days
are scored.

```bash
docker compose -f deploy/docker-compose.yml ps ml dbgrants backend
docker compose -f deploy/docker-compose.yml logs --tail 50 ml dbgrants
```

| What you see | Cause and fix |
| ------------ | ------------- |
| `ml` is `Created` and never starts | it waits for `dbgrants`, which waits for a healthy backend. Look at `logs backend` (a failed migration or a bad connection string stops the backend). |
| `Required artifacts missing` in the ML log | the artifacts folder is empty or incomplete. Run `deploy/scripts/preflight.sh`, copy the files (step 4), then `docker compose ... up -d ml`. |
| `ML_SHARED_SECRET` error at start | the variable is empty in `.env`. Set it and `up -d backend ml`. |
| `rejected the shared secret` in the backend log | backend and ML have different secrets (one was changed and only one restarted). `up -d backend ml`. |
| `permission denied for` or `password authentication failed` for the ML role | the grant is missing or the password changed. `docker compose ... up -d dbgrants` (or `deploy/scripts/apply-ml-grants.sh`) then `restart ml`. |
| `ml` restarts again and again, `OOMKilled` is true (`docker inspect -f '{{.State.OOMKilled}}' <project>-ml-1`) | out of memory. Check swap (`free -m`), stop other services on the VM, or lower `ML_PROPHET_N_JOBS` to 1. |

## Updating

```bash
cd /opt/simguard
deploy/scripts/backup.sh          # always before a release that has migrations
deploy/scripts/update.sh          # git pull --ff-only, preflight, build, up -d, wait for healthy
```

`update.sh` pulls the code (stops if the checkout has local changes), runs
`preflight.sh`, pulls the `postgres` and `caddy` images, builds backend, ML,
carrier mock and web app one after the other (so a 4 GB VM does not run out of
memory), runs `up -d` (only changed services restart; the backend applies new
migrations; `dbgrants` re-applies the ML role) and waits until every service is
healthy. It never runs `down`, never uses `-v` and never removes a volume.
`--no-pull` skips the git step, `--no-build` skips the builds. There is no
automatic rollback: restore from the backup if a release goes wrong
(`restore.sh`, below).

## Database

### Files

| File | What it is |
| ---- | ---------- |
| `deploy/docker-compose.yml` | the whole stack |
| `deploy/caddy/Caddyfile` | the proxy: one hostname, everything to the web app |
| `deploy/.env.example` | every variable, with no real values; copy to `deploy/.env` (gitignored) |
| `deploy/postgres/ml-readonly.sql` | idempotent read-only role for the ML service |
| `deploy/scripts/preflight.sh` | checks before the first start |
| `deploy/scripts/apply-ml-grants.sh` | runs the role file by hand |
| `deploy/scripts/backup.sh`, `restore.sh`, `test-restore.sh` | backups |
| `deploy/scripts/update.sh` | updates the stack in place, never removes volumes |
| `deploy/scripts/prepare-data.py` | builds the demo data pack in `deploy/data-pack/` (gitignored) |

Backend settings reach the container as environment variables set in the
compose file from `.env`; the connection string key is
`ConnectionStrings__DefaultConnection` (exactly what the backend reads), and
`Database__MigrateOnStartup=true` applies pending EF migrations at start. A
failed migration stops the start, so a half migrated database is never served.

### ML grants

The ML reader (`ml-service/app/readers/postgres_reader.py`) queries only the
view `ml_daily_usage`. The role gets `CONNECT`, `USAGE` on schema `public` and
`SELECT` on that view, nothing else, and is read only in every transaction.
Some migrations drop and recreate the view, which loses the grant; the
`dbgrants` job re-applies it at every `up`.

### Backup and restore

```bash
deploy/scripts/backup.sh                          # compressed dump in the pgbackups volume, newest 7 kept
deploy/scripts/restore.sh --list
deploy/scripts/restore.sh <dump> <target db>      # target is created if missing; --replace to overwrite tables
deploy/scripts/test-restore.sh                    # newest dump into a throwaway db, compares row counts, drops it
```

Daily backup from cron (as the user that runs docker):

```
17 2 * * * /opt/simguard/deploy/scripts/backup.sh >> /var/log/simguard-backup.log 2>&1
```

The dumps are in the `pgbackups` volume. Copy them off the VM too
(`docker run --rm -v <project>_pgbackups:/b -v "$PWD":/o alpine cp -a /b/. /o/`).
To restore over the live database: stop the backend (`docker compose -f
deploy/docker-compose.yml stop backend frontend`), run
`restore.sh <dump> <live db> --replace`, start them again. A dump contains the
accounts, so Admins survive a restore; it does not contain the Data Protection
keys (volume `dpkeys`), so also keep that volume.
`test-restore.sh --allow-growth` accepts live tables that gained rows since the
dump.

### Demo data pack

`python3 deploy/scripts/prepare-data.py` writes `deploy/data-pack/`: the numbered
import files in import order, `README.txt` (the order and the expected totals),
`mock-data/` and `mock.env`. Files over the import limits (25 MB, 250000 rows)
are split into numbered parts, imported in order. `--rotate-keys` replaces the
carrier keys; `--no-sessions` leaves the large per-session file out.

## Admin accounts survive

Accounts live in the database, in the `pgdata` volume. They survive container
restarts, image updates (`update.sh` never uses `down`, `-v` or `volume rm`) and
restores (a dump contains the accounts). Never run `docker compose down -v` on
the server: it deletes the volumes.

**Keep at least two Admin accounts**, on two different people, with MFA set up
on both. Then a lost authenticator or forgotten password is fixed by the other
Admin in the Admin screen (reset MFA, send sign-in details). The recovery below
is for when no Admin can sign in.

## I am locked out as Admin

Only for the server owner (shell access). Nothing here is reachable from the
web. Replace the values; use a new temporary password of 12 or more characters.

1. In `deploy/.env` set:

   ```
   SIMGUARD_RECOVER_ADMIN=true
   SIMGUARD_ADMIN_EMAIL=you@example.com
   SIMGUARD_ADMIN_PASSWORD=<new temporary password>
   ```

   (The compose file maps them to `SimGuard__Seed__RecoverAdmin`,
   `SimGuard__Seed__AdminEmail` and `SimGuard__Seed__AdminPassword`.)
2. Restart only the backend (not postgres):

   ```bash
   docker compose -f deploy/docker-compose.yml up -d --force-recreate --no-deps backend
   ```

   The backend log shows `ADMIN RECOVERY is on` and then `repaired` (account
   existed) or `created`. If it says `Nothing was changed`, a variable is missing.
3. Sign in with that email and the temporary password. You are asked to set up
   MFA again (scan the new QR code, keep the new recovery codes), then to choose
   a new password.
4. Set `SIMGUARD_RECOVER_ADMIN=false` and empty `SIMGUARD_ADMIN_PASSWORD` in
   `deploy/.env` and run the same command again. Check that
   `GET /api/audit-logs?action=admin.recovered` (Admin) shows one entry, and that it is you.

The recovery makes that account an enabled, unlocked Admin with MFA reset and
the given password, and writes the `system` audit entry `admin.recovered`
(no password). It runs only at startup and never when the variable is missing or
not exactly `true`. Details: `backend/docs/auth.md`, "Emergency Admin recovery".

## Decisions

Chosen unattended; change them if you disagree.

1. **Worktree and branch**: the work is in `simguard-deploy` on `feat/deploy-prep`, created from `feat/20-admin-dashboard-and-onboarding` with the owner's permission.
2. **Names**: compose project `sgdeploy` for proof runs; compose v2 names containers `sgdeploy-postgres-1` (hyphens) and volumes `sgdeploy_pgdata`, `sgdeploy_pgbackups`. The default project name in the file is `simguard`, overridable with `COMPOSE_PROJECT_NAME`.
3. **Dump format**: `pg_dump -Fc` (custom, compressed), restored with `pg_restore --no-owner`, so a dump restores into any database name.
4. **Retention**: the newest 7 dumps of the database, by modification time (`SIMGUARD_BACKUP_KEEP` overrides).
5. **Owner is a superuser** in the container (image default). Acceptable because postgres is never published; the ML role is the least privilege part.
6. **ML role** also has `default_transaction_read_only=on`, as a second line of defence behind the missing privileges.
7. **Grants are reapplied** on every `up` by the `dbgrants` job (and by `update.sh`) because some migrations recreate `ml_daily_usage`. The job runs after the backend is healthy and before the ML service starts, so one command brings everything up in order.
8. **Migrations in the backend** (not a separate job) behind `Database__MigrateOnStartup`, failing the start on error; on in Compose, off by default in the code.
9. **Data pack** reuses `ml-service/scripts/make_import_files.py` (imported, not copied) so the identifiers and columns cannot drift; the carrier mock gets the whole files, not the import parts. The 98 MB per-session file is copied into `mock-data/` unless `--no-sessions`.
10. **Production accounts need email**: in non-Development environments an Admin cannot set another person's password. For the local proof the Security Officer was created through a one-off container of the same backend image running in Development, with a temporary password, then removed; the stack itself ran in Production throughout.
11. **Recovery** replaces any other role of the account with Admin, and invalidates its tokens. Existing recovery codes are cleared with the MFA reset.
12. **Images are built on the VM** from the repository (no registry), tagged `<project>-<service>:latest`. `update.sh` builds one service at a time for the 4 GB limit.
13. **Windows proof**: the scripts were run from Git Bash on Windows with `MSYS_NO_PATHCONV=1`; `lib.sh` converts paths with `cygpath` when present. On Ubuntu that branch does nothing and was not exercised there.
14. **One hostname, one route**: Caddy sends everything to the web app. The browser never calls the API; the web app's server does, over the private network. So the API, ML service and mock have no public route at all.
15. **Real client address**: Caddy sets `X-Forwarded-For` to the visitor's address (it replaces what the visitor sent); the web app passes it to the API on sign-in, MFA and access-request calls (small change in `frontend/src/lib/auth/server.js`); the API trusts it only from the private subnet (`ForwardedHeaders:KnownNetworks`). Proved from inside the private network (the web app container calling the API with an address in `X-Forwarded-For`): 10 wrong sign-ins from one address got 401, the 11th and 12th got 429, another address was still answered with 401. Not proved end to end through Caddy on a machine with a real client address (the local test only sees Docker's gateway address).
16. **Fixed private subnet** `172.29.77.0/24` so the backend can name it as the trusted proxy network. Change `SIMGUARD_SUBNET` if it clashes.
17. **HTTPS redirect off in the backend** (`Hosting__UseHttpsRedirection=false`); TLS ends at Caddy. The default in code stays on.
18. **Production start checks**: outside Development the API refuses to start when `Jwt:Key` or `MlService:SharedSecret` is missing or a placeholder (empty, one repeated character, or containing `changeme`, `replace`, `example`, `your-`, `<`, `>` and similar). `preflight.sh` applies the same rule to `.env`, plus a minimum length of 32. The ML service already refused to start in Production without its secret.
19. **Data Protection keys** in the volume `dpkeys` at `/keys` (`Hosting__DataProtectionKeysPath`), so saved carrier API keys survive recreated containers. Proved by restarting the whole stack and testing a saved key without sending it again.
20. **Plain http for the demo feed**: the backend only accepts https feed addresses (loopback excepted). The demo mock is on the private network over http, so `CarrierFeed__AllowInsecureHttp` is `true` by default in Compose (`CARRIER_FEED_ALLOW_INSECURE_HTTP`). Set it to `false` once real carriers replace the mock. The mock's own admin routes have no key; they are reachable only inside the private network.
21. **Carrier mock date**: `CarrierMock:StartDate` now also accepts `last` (the last day in the data); Compose defaults to `last` through `CARRIER_MOCK_DATE`. The mock's own default (first day) is unchanged outside Compose.
22. **ML image**: CPU only (the NVIDIA libraries that xgboost pulls were removed, about 350 MB), without mlflow and the test packages; model artifacts are mounted read-only from `SIMGUARD_ML_ARTIFACTS_DIR`, never baked in; runs as user `ml`. Size 1.59 GB.
23. **Users in containers**: backend and mock run as the image's `app` user, the web app as `node`, ML as `ml`. Postgres and Caddy keep their images' defaults (Caddy runs as root inside its container to bind ports 80 and 443).
24. **Memory limits** (ceilings, not reservations): backend 1 GB, ML 2 GB, web app 768 MB, mock 1 GB. Measured at rest on the full stack: postgres 102 MB, backend 121 MB, ML 142 MB, web app 47 MB, mock 77 MB, Caddy 13 MB; the backend peaked at 293 MB and the ML service at 176 MB during a scoring run.
25. **Health checks**: the .NET images have no curl, so they use `bash` and `/dev/tcp`; ML uses Python's `urllib`; the web app and Caddy use `wget`.
26. **Local proof settings**: `DOMAIN=localhost`, `CADDY_GLOBAL_OPTIONS=local_certs` (Caddy's own certificate authority instead of Let's Encrypt), `HTTP_PORT=8088`, `HTTPS_PORT=8443`. The redirect from http 8088 points at https on 443 (a local-only effect of the port mapping); the proof used https on 8443 directly. No local compose override exists any more; Postgres is never published.
27. **Email tile**: with `BREVO_API_KEY` blank, System health shows Email as `Needs attention`; this is expected and was left that way for the proof.
28. **Smokes** were run from the repository against a one-off Development container of the stack's backend image (published on 127.0.0.1 only, removed afterwards), because the Production stack does not publish the API and Production refuses the temporary passwords the smokes use. Auth and roles used the throwaway database `sg_smoke`, dropped afterwards.
29. **Recovery in the stack**: the emergency recovery was proved against the running Production stack with the commands in this file: the backend log showed `ADMIN RECOVERY is on` and `repaired`, the old password stopped working, the Admin signed in with the temporary password, enrolled MFA again and changed the password, one `admin.recovered` audit entry exists and it holds no password. The variables were then removed and the backend recreated (no recovery message in its log).
