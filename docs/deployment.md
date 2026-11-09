# Deployment

Issues #22 to #26. One Ubuntu VM (4 GB RAM, 2 GB swap) runs the whole system:
the shared Caddy (`deploy/scripts/edge.sh up`, once per VM) and one stack per
organisation (`docker compose -f deploy/docker-compose.yml up -d --build`, or
`deploy/scripts/create-org.sh` for the second and later ones). One organisation
needs only these two commands; running several is described in "Several
organisations on one VM".

| Service | What it is | Reachable from |
| ------- | ---------- | -------------- |
| `caddy` | not part of the organisation stack: the shared Caddy (`deploy/shared`), the only public service, ports 80 and 443, certificates from Let's Encrypt | the internet |
| `frontend` | Next.js web app (port 3000); it calls the API on behalf of the browser | `caddy` |
| `backend` | .NET 10 API (port 8080); applies migrations at start | `frontend` |
| `ml` | Python scoring service (port 8001), read-only database role, shared secret | `backend` |
| `carriermock` | the demo carrier usage feed (port 8080), data in a mounted folder | `backend` |
| `postgres` | PostgreSQL 16 (port 5432), data in the volume `pgdata` | `backend`, `ml`, `dbgrants` |
| `dbgrants` | one-shot job: creates or refreshes the ML role after the backend has migrated | exits |

Only the shared Caddy publishes ports. Everything else lives on the stack's
private network `internal` (subnet `172.29.77.0/24`, change with
`SIMGUARD_SUBNET`). The `frontend` also joins the shared network `simguard-edge`
(under the alias `<project>-web`) so Caddy can reach it; no other service is on
that network. Start order (health based): postgres, backend, dbgrants, ml;
backend, frontend. Every service has a health check and `restart: unless-stopped`.

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
address; Let's Encrypt needs it. `preflight.sh` checks this. For more than one
organisation also add a wildcard record `*.<domain>` to the same address (see
"Several organisations on one VM").

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
# another folder: --out DIR or the environment variable SIMGUARD_DATA_PACK_OUT

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
| `HTTP_PORT`, `HTTPS_PORT`, `CADDY_GLOBAL_OPTIONS` | read by the shared Caddy (`edge.sh`) from `deploy/orgs/edge.env`, else `deploy/.env`; local tests only, leave unset on the VM |
| `SIMGUARD_IMAGE_PREFIX`, `SIMGUARD_EDGE_NETWORK`, `SIMGUARD_ORG_HOST` | several organisations; the defaults suit one organisation (see that section) |

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
deploy/scripts/edge.sh up                      # the shared Caddy and the edge network, once per VM
docker compose -f deploy/docker-compose.yml up -d --build
docker compose -f deploy/docker-compose.yml ps
```

The shared Caddy needs a site entry for the organisation: register it with
`deploy/scripts/adopt-org.sh main simguard "$PWD/deploy/.env" <domain>` and run
`deploy/scripts/edge.sh reload` (see "Several organisations on one VM"). On a
fresh VM the simplest route is `create-org.sh` for every organisation, the
first included, instead of `deploy/.env`: it does steps 4 to 7 for you.

The first build takes a while on a small VM (the web app and the API are
compiled on the VM; budget 20 to 30 minutes). Later starts take about 20
seconds. All services show `healthy`; `dbgrants` shows `Exited (0)`, which is
correct. Caddy fetches the certificate on the first request to the host:
`docker compose -p simguard-edge -f deploy/shared/docker-compose.yml logs caddy | grep -i certificate`.

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
`preflight.sh`, pulls the `postgres` image, builds backend, ML,
carrier mock and web app one after the other (so a 4 GB VM does not run out of
memory), runs `up -d` (only changed services restart; the backend applies new
migrations; `dbgrants` re-applies the ML role) and waits until every service is
healthy. It never runs `down`, never uses `-v` and never removes a volume.
`--no-pull` skips the git step, `--no-build` skips the builds. There is no
automatic rollback: restore from the backup if a release goes wrong
(`restore.sh`, below). With several organisations: `SIMGUARD_ORG=<slug>
deploy/scripts/update.sh` for one, `deploy/scripts/update-all.sh` for all
(one pull, one build, then each stack in turn). A stack that still has its own
caddy container is refused until it has been moved (see "Moving the live
stack").

## Database

### Files

| File | What it is |
| ---- | ---------- |
| `deploy/docker-compose.yml` | one organisation's stack (no Caddy, no published port) |
| `deploy/shared/docker-compose.yml`, `deploy/shared/Caddyfile` | the shared Caddy; the site entries come from `deploy/orgs/caddy/sites.caddy` |
| `deploy/orgs/` | git-ignored state: `orgs.list`, `<slug>.env` (secrets, mode 600), `edge.env`, `defaults.env`, `caddy/sites.caddy` |
| `deploy/scripts/create-org.sh`, `remove-org.sh`, `list-orgs.sh`, `adopt-org.sh` | organisations |
| `deploy/scripts/edge.sh`, `render-caddy.sh` | the shared Caddy and its generated site entries |
| `deploy/scripts/backup-all.sh`, `update-all.sh` | every organisation |
| `deploy/scripts/prove-orgs.py`, `prove-migration.py`, `deploy/test/docker-compose.development.yml` | local proofs (the Development override is for tests only, never for a server) |
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
deploy/scripts/backup.sh [slug]                   # compressed dump in the pgbackups volume, newest 7 kept (slug: see "Several organisations")
deploy/scripts/restore.sh --list
deploy/scripts/restore.sh <dump> <target db>      # target is created if missing; --replace to overwrite tables
deploy/scripts/test-restore.sh [slug]             # newest dump into a throwaway db, compares row counts, drops it
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
`mock-data/` and `mock.env`. The 1000 employees have realistic Kenyan names
(`deploy/scripts/demo_people.py`: seeded by the SIM id, so every run gives the
same unique names, emails `first.last@example.com`, job roles that fit each
department). The same email is in the employees, allocations and
`mock-data/sim_mapping.csv`. An existing `mock.env` is never overwritten.
Files over the import limits (25 MB, 250000 rows)
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

## Several organisations on one VM

Each organisation is a separate SimGuard: its own database, volumes, secrets,
carrier mock and keys, data folders, private network, first Admin and
subdomain. They share the VM, the images (one build for all), and one Caddy.
A Security Officer or Auditor of one organisation can only ever reach that
organisation's data (isolation model and limits below).

```
internet --443--> shared Caddy --edge network--> <project>-web (frontend of org 1)
                  (deploy/shared)             \-> <project>-web (frontend of org 2)
org 1: frontend, backend, ml, carriermock, postgres   on its own network  (172.29.77.0/24)
org 2: frontend, backend, ml, carriermock, postgres   on its own network  (172.29.78.0/24)
```

### DNS (required)

Two kinds of record, both to the VM's public address:

| Record | For |
| ------ | --- |
| `A  <domain>` | the first organisation (the live system keeps its address) |
| `A  *.<domain>` (wildcard) | every other organisation, at `<slug>.<domain>` |

`create-org.sh` does not create DNS records. Without the wildcard (or a record
for the slug) Let's Encrypt cannot issue the certificate and the new address
does not open. Caddy issues one certificate per host on its first request; Let's
Encrypt allows 50 new certificates a week per registered domain.

### Create, list, remove

```bash
cd /opt/simguard
export DOMAIN=sim-guard.app                     # or put it in deploy/orgs/defaults.env (mode 600)
deploy/scripts/edge.sh up                       # the shared Caddy, once per VM
deploy/scripts/create-org.sh acme owner@acme.example
deploy/scripts/list-orgs.sh                     # organisation, URL, health
deploy/scripts/remove-org.sh acme               # stops it, removes its Caddy entry, ASKS about its volumes
```

`create-org.sh <slug> <first-admin-email>`:

- slug: 1 to 20 characters, lower case letters, digits and hyphens, starting with a letter. The first organisation on a VM is served at the apex `DOMAIN`; every later one at `<slug>.DOMAIN`.
- creates `/srv/simguard/<slug>/` with `mock-data/`, `ml-artifacts/` and `mock.env` (this organisation's own three carrier keys, made with `openssl`);
- writes `deploy/orgs/<slug>.env` (mode 600, git-ignored) with every secret generated by `openssl rand` (database owner and ML passwords, `JWT_KEY`, `ML_SHARED_SECRET`, the first Admin password) and a free subnet (`172.29.78.0/24` and up), sign-in URL `https://<host>/login`, project name `simguard-<slug>`;
- builds the images only when they are missing, starts the stack, waits until it is healthy, and only then adds the entry to `deploy/orgs/orgs.list`, regenerates `deploy/orgs/caddy/sites.caddy` and reloads Caddy;
- prints the URL and one line saying where the first Admin password is: `SIMGUARD_ADMIN_PASSWORD` in `deploy/orgs/<slug>.env`. Nothing secret is printed. Sign in, set up MFA, choose a new password, then empty that line in the env file (same as step 8 above) and run `SIMGUARD_ORG=<slug> deploy/scripts/update.sh --no-pull --no-build`.

The mock data and model files are copied from `/srv/simguard/mock-data` and
`/srv/simguard/ml-artifacts` (the first organisation's folders; hard links when
possible) or from `SIMGUARD_MOCK_SOURCE` and `SIMGUARD_ML_ARTIFACTS_SOURCE`.
Other settings can go in `deploy/orgs/defaults.env`: `DOMAIN`, `BREVO_API_KEY`,
`BREVO_SENDER_EMAIL`, `SIMGUARD_ORGS_ROOT`. If it fails, nothing is published:
the message names the log and `remove-org.sh --force <slug>` clears the
half-made stack, its volumes, env file and folder.

`remove-org.sh <slug>` stops the stack and removes its Caddy entry, then asks
(type `yes`) before deleting its volumes (database, Data Protection keys,
backups) and again before deleting its env file and folder. With no terminal
every question is answered no. The first organisation cannot be removed while
others exist.

### Backups of every organisation

```bash
deploy/scripts/backup.sh [slug]            # default: the first organisation
deploy/scripts/test-restore.sh [slug]
deploy/scripts/backup-all.sh               # every organisation in turn; one failure does not stop the rest
```

Cron (as the user that runs docker), daily backup of all and a weekly restore
test of each:

```
17 2 * * *  /opt/simguard/deploy/scripts/backup-all.sh >> /var/log/simguard-backup.log 2>&1
47 3 * * 0  for s in $(awk '!/^#/ && NF {print $1}' /opt/simguard/deploy/orgs/orgs.list); do /opt/simguard/deploy/scripts/test-restore.sh "$s" --allow-growth; done >> /var/log/simguard-restore-test.log 2>&1
```

Each organisation's dumps are in its own `pgbackups` volume. Restore into
another organisation's database by name only on purpose:
`SIMGUARD_ORG=<slug> deploy/scripts/restore.sh <dump> <db>`.

### Moving the live stack onto the new layout

The live stack (project `simguard`, settings in `deploy/.env`, volumes
`simguard_pgdata`, `simguard_dpkeys`, `simguard_pgbackups`, certificates in
`simguard_caddy_data`) keeps all its volumes and variables. Only the Caddy
container moves out. The steps keep the old Caddy serving until the last
moment; the only gap is the few seconds between the last two commands.
Replace `sim-guard.app` with your `DOMAIN` and run on the VM:

```bash
cd /opt/simguard
deploy/scripts/backup.sh                                   # 1. safety net (works before and after the pull)
git pull --ff-only                                         # 2. new files; running containers are not touched
deploy/scripts/adopt-org.sh main simguard "$PWD/deploy/.env" sim-guard.app   # 3. register it (only writes deploy/orgs/orgs.list)
deploy/scripts/edge.sh network                             # 4. the shared network, nothing else yet
docker compose -f deploy/docker-compose.yml up -d          # 5. only the frontend is recreated (it joins the network); the old Caddy still serves
# 6. the shared Caddy reuses the old certificate volumes, so no new certificate is needed
{ grep -E '^(HTTP_PORT|HTTPS_PORT|CADDY_GLOBAL_OPTIONS)=' deploy/.env || true
  echo 'SIMGUARD_CADDY_DATA_VOLUME=simguard_caddy_data'
  echo 'SIMGUARD_CADDY_CONFIG_VOLUME=simguard_caddy_config'; } > deploy/orgs/edge.env
docker rm -f simguard-caddy-1                              # 7. frees ports 80 and 443 (the gap starts)
deploy/scripts/edge.sh up                                  # 8. the shared Caddy takes over (the gap ends)
deploy/scripts/list-orgs.sh                                # 9. main: https://sim-guard.app, healthy
curl -sI https://sim-guard.app | head -n1                  # 10. HTTP/2 200
```

Step 5 prints a warning about an orphan container (the old `caddy`); that is
the old Caddy and it is expected until step 7. Do not use `--remove-orphans`
there: it would stop the old Caddy before the new one is ready. If the old
compose file project name was not `simguard`, replace it in steps 3 and 7
(`docker ps --format '{{.Names}}' | grep caddy`).

Rollback (before step 9 you can undo all of it): `deploy/scripts/edge.sh down`,
`git checkout <commit before the pull> -- deploy`, `docker compose -f
deploy/docker-compose.yml up -d` starts the old Caddy again on the same
volumes; then `git checkout <branch> -- deploy` when you want to try again.

What was proved locally (a stack in the old layout with data, then these
commands): the Admin created before the move signed in afterwards with the same
password and authenticator key; a carrier whose feed API key is stored
encrypted with the Data Protection keys still existed and its saved key still
worked (so database and `dpkeys` are intact); the container ids of postgres,
backend, ML and mock did not change and the volume list was identical; Caddy
presented the same certificate fingerprint. A second organisation was then
created next to it. The proof scripts are `deploy/scripts/prove-migration.py`
(before and after) and the commands above.

### Memory

Measured with two organisations running idle (no scoring run, no builds):
about 450 MB per stack (postgres 45, backend 136, ML 142, mock 74 to 85, web app 48)
plus 15 MB for Caddy, 919 MB in all (`docker stats`). The configured ceilings are larger (backend 1 GB, ML 2 GB,
web app 768 MB, mock 1 GB per stack), and a scoring run and the image builds
need more than the idle figure.

| VM | Guidance |
| -- | -------- |
| 4 GB RAM and 2 GB swap | 2 organisations. Build the images once (they are shared) and never while both are scoring. Keep `ML_PROPHET_N_JOBS=1` (the default `create-org.sh` writes). |
| 8 GB RAM | 3 to 4 organisations. |

Every organisation starts its daily scoring run at 02:00 Nairobi time, so all
of them score at the same moment. With 3 or more on a small VM, watch
`docker stats` during the first nights. Two concurrent scoring runs were not
measured locally.

### Isolation model and its limits

What is separate per organisation:

- the database: its own postgres container, volume and passwords, with one database in it; there is no shared database server and no cross-database role;
- the secrets: its own `JWT_KEY` (a sign-in token of one organisation is refused by another), `ML_SHARED_SECRET`, ML role, Data Protection keys (volume `dpkeys`, which encrypts the saved carrier feed keys), carrier keys and first Admin;
- the network: each stack has its own bridge network; its backend, ML service, mock and database are on no other network, and nothing of them is published on the host;
- files: its own folder under `/srv/simguard/<slug>` and env file (mode 600).

What is shared, and therefore the limits of the model:

- **The edge network.** The web app of every organisation joins it, so one web app can open a connection to another organisation's web app on port 3000. The web app holds no data and no secret of its own, and has no route to another organisation's backend, but it is the one shared surface. Caddy also sits there and reaches only the web apps.
- **The host.** One kernel, one Docker daemon, one disk, one memory pool. Anyone with root or docker access on the VM can read every organisation's env file, volumes and backups. A container escape or a compromised host defeats all isolation.
- **The code.** All organisations run the same images, so a vulnerability affects all of them, and `update-all.sh` updates all of them to the same release.
- **Resources.** A busy organisation can slow the others (no CPU limits; memory ceilings only).
- **Backups** are in volumes on the same VM; copy them off the VM.
- **Email.** If one Brevo key and sender are put in `defaults.env`, every organisation sends from the same sender.
- **Domain level.** Separate subdomains give separate cookies (a session cookie is per host), but they are not a security boundary against someone who controls DNS or Caddy.

What was checked locally with two organisations, `prove-orgs.py` (Decisions list the setup): both addresses answer over a certificate that verifies against Caddy's local CA; each seeded Admin signs in (MFA enrolment included) and the other organisation's Admin credentials are refused; an Officer in each; disjoint slices of the data pack imported into each (preview and commit: 4 departments, 150 employees, 150 SIMs, 150 allocations, 0 errors, all inserted); Officer A sees exactly A's 150 SIMs and 150 employees and none of B's, by listing and by searching 20 of B's ICCIDs; A's session cookie sent to B's address gets 401; in the databases A holds 150 SIMs and 0 of B's and the reverse, and each postgres server holds only `postgres` and its own `simguard`; the database volumes differ; none of B's carrier keys appears in A's container settings or env file, A's mock accepts its own key and refuses B's with 401, A's backend cannot resolve B's mock, backend or database by name and cannot open a connection to B's backend or database by IP; no container publishes a host port except Caddy (80 and 443); a backend is on exactly one network; the edge network holds only Caddy and the two web apps; Caddy reaches a web app by its alias and cannot reach any backend.

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

### Several organisations (branch `feat/multi-org-stacks`)

30. **Demo people**: `demo_people.py` builds names from lists of Kikuyu, Luo, Luhya, Kalenjin, Kamba, Kisii, Meru, coastal, Somali and Kenyan-Asian names plus common international first names. They are invented combinations. A generator seeded with `simguard-demo-person-<sim id>` picks each name, redrawing on a duplicate, so a run gives the same 1000 unique people. Emails use `example.com`. `ml-service/scripts/make_import_files.py` was not changed: the pack script overrides the email in the mapping it already loads.
31. **Data pack location**: written to `C:\Users\ADMIN\Desktop\projects\simguard\deploy\data-pack` (gitignored, outside the worktree) through `SIMGUARD_DATA_PACK_OUT`. The existing `mock.env` there was left untouched (same SHA-256 before and after). The mock data files and the five CSV files were regenerated.
32. **Part A import check**: the container `simguard-db` was stopped and had no host port (5433 was not published by anything), and it was not touched or started. A throwaway `postgres:16` container on `127.0.0.1:55432` with the database `sg_check_a` and the API in Development on `127.0.0.1:5871` were used, then removed. Departments, employees, SIMs and allocations were previewed through the real import API: 4, 1000, 1000 and 1000 rows, 0 errors, all inserted. The usage file was not imported (it queues a scoring run and the ML service was not running).
33. **One compose file for every organisation**: a stack is selected by its env file and project name. The old variables are all unchanged; the new ones (`SIMGUARD_IMAGE_PREFIX`, `SIMGUARD_EDGE_NETWORK`, `SIMGUARD_ORG_HOST`, `SIMGUARD_EDGE_PROJECT`, `SIMGUARD_CADDY_*`) have defaults that reproduce the old behaviour. `HTTP_PORT`, `HTTPS_PORT` and `CADDY_GLOBAL_OPTIONS` are now read by the shared Caddy (from `deploy/orgs/edge.env`, else `deploy/.env`), so a live `deploy/.env` that sets them keeps working.
34. **Edge network**: created by `edge.sh` (or `lib.sh`), external for every stack, so any stack can start and stop alone. The frontend's alias is `<project>-web`, derived from the project name so no new variable is needed and the live stack becomes `simguard-web`. The backend, ML service, mock and database never join it.
35. **Naming**: project `simguard-<slug>` (`SIMGUARD_PROJECT_PREFIX` changes the prefix, the local proof used `sgorg`), volumes `<project>_pgdata` and so on from compose, containers `<project>-<service>-1`. Images are shared by all organisations under `SIMGUARD_IMAGE_PREFIX` (default `simguard`): one build serves all, and every organisation runs the same release.
36. **First organisation at the apex**: the first line of `deploy/orgs/orgs.list` is served at `DOMAIN`, later ones at `<slug>.DOMAIN`. `create-org.sh` refuses to claim the apex while an unregistered stack named `simguard` runs on the machine.
37. **Own folders per organisation**: mock data (hard links when possible, else a copy), model artifacts (4.5 MB, copied) and carrier keys. Carrier keys are made by `create-org.sh` (24 random bytes per carrier, hex), not by `prepare-data.py`.
38. **Subnets**: the live stack keeps `172.29.77.0/24`; `create-org.sh` takes the first free `172.29.N.0/24` from N=78 that no Docker network and no env file uses.
39. **ML workers**: `create-org.sh` writes `ML_PROPHET_N_JOBS=1` (compose default is 2) so several stacks fit on a small VM.
40. **Publish last**: the Caddy entry is added only after the stack is healthy, so a failed creation is never public. On failure the folders, volumes and env file stay for inspection; `remove-org.sh --force <slug>` clears them.
41. **Removal**: `remove-org.sh` uses `docker compose down` (no `-v`), asks before the volumes and again before the env file and folder, treats no terminal as no, deletes only volumes named `<project>_*` (so the shared certificate volumes are never touched), and keeps one that is in use.
42. **First Admin password**: stored in the organisation's env file (owner only) because the first Admin has to be seeded from the environment. The script prints only the file name. The docs tell the owner to empty it after the first sign-in. Every organisation must have a different first Admin email (checked against all env files).
43. **Update**: `update.sh` refuses a stack that still has its own `caddy` container, because `up --remove-orphans` would remove it and take the site down before the shared Caddy exists. `update-all.sh` pulls and builds once, then updates each stack with `--no-pull --no-build`.
44. **Edge settings fallback**: `edge.sh` reads `deploy/orgs/edge.env`, else `deploy/.env`. It always passes `-p` for its project so a `COMPOSE_PROJECT_NAME` left in the environment by another script cannot rename it (this happened once in the first local run and is fixed).
45. **Officer creation in the proof**: stacks are Production (Admin cannot choose another person's password, and Brevo was blank so no invitation could be read). The local proof therefore starts the backends with `deploy/test/docker-compose.development.yml` (`ASPNETCORE_ENVIRONMENT=Development`, sign-in rate limit raised), which allows `temporaryPassword` on `POST /api/users`. That is the Development-only shortcut. It is passed with `SIMGUARD_COMPOSE_EXTRA` and must never be used on a server. Nothing else differs.
46. **Local proof settings**: `DOMAIN=sgorg.localhost`, project prefix `sgorg` (projects `sgorg-a`, `sgorg-b`, `sgorg-edge`), network `sgorg-edge-net`, `local_certs`, ports 8088 and 8443, images `sgorg-*`, `Brevo__ApiKey` blank. The proof script connects to 127.0.0.1 but sends the organisation's host as SNI and `Host` (Windows does not resolve `*.localhost`), and verifies the certificate against the root certificate copied from the Caddy container.
47. **Migration proof**: a stack was started from the old layout (`git archive` of the commit before this branch, project `sgorg-live`, with data), then the checkout was replaced by this branch's files and the documented commands were run. The Development override was used before and after so an Officer could exist; it changes only the backend environment, and the backend container was not recreated by the migration.
48. **Not tested here**: real Let's Encrypt issuance and wildcard DNS (the local proof uses Caddy's own CA), Ubuntu (everything ran from Git Bash on Windows with Docker Desktop), a 4 GB VM under load, two scoring runs at the same time, sending email, `update.sh` with a real `git pull`, the cron lines.
