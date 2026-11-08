# Deployment

Issue #22. This file grows as services are added to `deploy/docker-compose.yml`.
Today it covers the database only, on one Linux VM (Ubuntu) with docker, the
compose plugin, bash and standard tools.

## Database

### Files

| File | What it is |
| ---- | ---------- |
| `deploy/docker-compose.yml` | postgres 16, named volumes `pgdata` and `pgbackups`, health check, `restart: unless-stopped`, no published ports |
| `deploy/docker-compose.local.yml` | local proof only: publishes postgres on `127.0.0.1:5439` |
| `deploy/.env.example` | variable names; copy to `deploy/.env` (gitignored) |
| `deploy/postgres/ml-readonly.sql` | idempotent read-only role for the ML service |
| `deploy/scripts/apply-ml-grants.sh` | runs that file after the migrations |
| `deploy/scripts/backup.sh`, `restore.sh`, `test-restore.sh` | backups |
| `deploy/scripts/update.sh` | pulls newer images and restarts in place, never removes volumes |
| `deploy/scripts/prepare-data.py` | builds the demo data pack in `deploy/data-pack/` (gitignored) |

### Variables (`deploy/.env`)

| Variable | Meaning |
| -------- | ------- |
| `SIMGUARD_DB_NAME` | database name |
| `SIMGUARD_DB_OWNER`, `SIMGUARD_DB_OWNER_PASSWORD` | owner account; the backend connects with it (the image makes it a superuser inside the container, which is not published) |
| `SIMGUARD_ML_USER`, `SIMGUARD_ML_PASSWORD` | the ML service's read-only role |
| `COMPOSE_PROJECT_NAME` | optional; prefix of containers and volumes (default `simguard`) |

Backend variables (environment only, never committed):

| Variable | Meaning |
| -------- | ------- |
| `ConnectionStrings__DefaultConnection` | the connection string. This is the exact key the backend reads (`builder.Configuration.GetConnectionString("DefaultConnection")`). On the compose network: `Host=postgres;Port=5432;Database=<name>;Username=<owner>;Password=<owner password>` |
| `Database__MigrateOnStartup` | `true` applies pending EF migrations when the backend starts (default off) |
| `SimGuard__Seed__AdminEmail`, `SimGuard__Seed__AdminPassword` | first Admin, used only while there are no accounts |
| `Jwt__Key` | at least 32 random bytes; the API refuses to start without it outside Development |
| `ML_DATABASE_URL` | for the ML service: `postgresql://<ML user>:<ML password>@postgres:5432/<name>` |

### Start and migrate

```bash
cp deploy/.env.example deploy/.env      # then fill in the passwords (openssl rand -hex 24)
docker compose -f deploy/docker-compose.yml up -d
```

Migrations run inside the backend at startup, with the backend's own database
user, when `Database__MigrateOnStartup=true`. A failed migration stops the
start, so a half migrated database is never served. `dotnet ef database update`
still works as before; the option is off unless the variable is set.

### ML grants

The ML reader (`ml-service/app/readers/postgres_reader.py`) queries only the
view `ml_daily_usage`. The role gets `CONNECT`, `USAGE` on schema `public` and
`SELECT` on that view, nothing else, and is read only in every transaction.

```bash
deploy/scripts/apply-ml-grants.sh
```

Run it after the first migration and again after every update: some migrations
drop and recreate the view, which loses the grant (`update.sh` does it for you).
It is safe to run any number of times and sets the role's password from
`SIMGUARD_ML_PASSWORD`.

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
To restore over the live database: stop the backend, run
`restore.sh <dump> <live db> --replace`, start the backend.
`test-restore.sh --allow-growth` accepts live tables that gained rows since the
dump.

### Demo data pack

```bash
python3 deploy/scripts/prepare-data.py        # writes deploy/data-pack/ (needs ml-service/data/synthetic_cdr_daily_agg.csv)
cat deploy/data-pack/README.txt               # the import order
```

Sign in as a Security Officer, open Imports and import the files in the order of
`README.txt` (preview, then commit; wait for each commit). Files over the import
limits (25 MB, 250000 rows) are split into numbered parts, imported in order.
`deploy/data-pack/mock-data/` and `mock.env` (carrier names with generated keys,
mode 0600, never printed) are for the carrier mock service added later.
`--rotate-keys` replaces the keys.

### First Admin

Set `SimGuard__Seed__AdminEmail` and `SimGuard__Seed__AdminPassword` (12+
characters) on the backend and start it. With no accounts, the first Admin is
created. The first sign-in asks for MFA enrolment and a password change. After
that sign-in, remove `SimGuard__Seed__AdminPassword` from the environment and
restart only the backend. Create a second Admin straight away (below).

Other accounts are invited by an Admin and the sign-in details are emailed, so
email (Brevo) must be configured on the server for that; without it the invite
works but nobody receives the password.

### Admin accounts survive

Accounts live in the database, in the `pgdata` volume. They survive container
restarts, image updates (`update.sh` never uses `down`, `-v` or `volume rm`) and
restores (a dump contains the accounts). Never run `docker compose down -v` on
the server: it deletes the volumes.

**Keep at least two Admin accounts**, on two different people, with MFA set up
on both. Then a lost authenticator or forgotten password is fixed by the other
Admin in the Admin screen (reset MFA, send sign-in details). The recovery below
is for when no Admin can sign in.

### I am locked out as Admin

Only for the server owner (shell access). Nothing here is reachable from the
web. Replace the values; use a new temporary password of 12 or more characters.

1. Set the variables for the backend only. In `deploy/.env` (or the backend's
   environment file) add:

   ```
   SimGuard__Seed__RecoverAdmin=true
   SimGuard__Seed__AdminEmail=you@example.com
   SimGuard__Seed__AdminPassword=<new temporary password>
   ```

2. Restart only the backend (not postgres):

   ```bash
   docker compose -f deploy/docker-compose.yml up -d --force-recreate --no-deps backend
   ```

   The backend log shows `ADMIN RECOVERY is on` and then `repaired` (account
   existed) or `created`. If it says `Nothing was changed`, a variable is missing.
3. Sign in with that email and the temporary password. You are asked to set up
   MFA again (scan the new QR code, keep the new recovery codes), then to choose
   a new password.
4. Remove the three variables (`RecoverAdmin`, `AdminEmail` and
   `AdminPassword`) and restart only the backend again with the same command.
   Check that `GET /api/audit-logs?action=admin.recovered` (Admin) shows one
   entry, and that it is you.

The recovery makes that account an enabled, unlocked Admin with MFA reset and
the given password, and writes the `system` audit entry `admin.recovered`
(no password). It runs only at startup and never when the variable is missing or
not exactly `true`. Details: `backend/docs/auth.md`, "Emergency Admin recovery".
Until the backend service exists in the compose file, run the same variables
against the backend process you run on the VM.

### Decisions

Chosen unattended; change them if you disagree.

1. **Worktree and branch**: the work is in `simguard-deploy` on `feat/deploy-prep`, created from `feat/20-admin-dashboard-and-onboarding` with the owner's permission.
2. **Names**: compose project `sgdeploy` for proof runs; compose v2 names containers `sgdeploy-postgres-1` (hyphens) and volumes `sgdeploy_pgdata`, `sgdeploy_pgbackups`. The default project name in the file is `simguard`, overridable with `COMPOSE_PROJECT_NAME`.
3. **Dump format**: `pg_dump -Fc` (custom, compressed), restored with `pg_restore --no-owner`, so a dump restores into any database name.
4. **Retention**: the newest 7 dumps of the database, by modification time (`SIMGUARD_BACKUP_KEEP` overrides).
5. **Owner is a superuser** in the container (image default). Acceptable because postgres is never published; the ML role is the least privilege part.
6. **ML role** also has `default_transaction_read_only=on`, as a second line of defence behind the missing privileges.
7. **Grants are reapplied** by `update.sh` because some migrations recreate `ml_daily_usage`.
8. **Migrations in the backend** (not a separate job) behind `Database__MigrateOnStartup`, failing the start on error.
9. **Data pack** reuses `ml-service/scripts/make_import_files.py` (imported, not copied) so the identifiers and columns cannot drift; the carrier mock gets the whole files, not the import parts. The 98 MB per-session file is copied into `mock-data/` unless `--no-sessions`.
10. **Production accounts need email**: in non-Development environments an Admin cannot set another person's password, so for the local proof the Security Officer was created while the API ran in Development, then the API was restarted as Production for the imports.
11. **Recovery** replaces any other role of the account with Admin, and invalidates its tokens. Existing recovery codes are cleared with the MFA reset.
12. **update.sh** only pulls and `up -d`; it has no rollback. Nothing is pushed anywhere by these scripts.
13. **Windows proof**: the scripts were run from Git Bash on Windows with `MSYS_NO_PATHCONV=1`; `lib.sh` converts paths with `cygpath` when present. On Ubuntu that branch does nothing and was not exercised there.
