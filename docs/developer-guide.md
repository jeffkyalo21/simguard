# SimGuard developer guide

This guide is for someone who has to build, run, test, change and deploy
SimGuard. It gives the order of things and links to the detailed documents
instead of copying them. If a detail here disagrees with a linked document, the
linked document wins: please fix this guide.

If you only use the website, read the [user guide](user-guide.md).

How the commands were checked: commands marked "checked" were run while this
guide was written, on Windows with Git Bash. Commands marked "read only" were
checked by reading the script or its help text, and were not run. Anything
that could not be checked is marked "not verified". Nothing was run against a
live server.

Contents:

1. [Architecture in one page](#1-architecture-in-one-page)
2. [Repository layout](#2-repository-layout)
3. [Prerequisites and versions](#3-prerequisites-and-versions)
4. [Setup and configuration](#4-setup-and-configuration)
5. [Run locally](#5-run-locally)
6. [The data pipeline](#6-the-data-pipeline)
7. [Tests](#7-tests)
8. [Adding a screen or an endpoint](#8-adding-a-screen-or-an-endpoint)
9. [ML retraining](#9-ml-retraining)
10. [Deployment](#10-deployment)
11. [Secrets](#11-secrets)
12. [Git workflow](#12-git-workflow)
13. [Known limitations](#13-known-limitations)

## 1. Architecture in one page

```
browser --https--> Caddy (the only public service, ports 80 and 443)
                     |
                     v
                  frontend (Next.js, port 3000)       the browser never calls the API
                     |  private network               and never sees a token
                     v
                  backend (ASP.NET Core 10, port 8080) --> postgres (PostgreSQL 16)
                     |            |
                     |            +--> ml (FastAPI, port 8001) --> postgres (read only role)
                     |
                     +--> carrier usage feed over https (or plain http on the private network)
                           - carriermock (in the stack, demo)
                           - the carrier server (its own small server, demo)
```

| Part | What it does |
| ---- | ------------ |
| `frontend` | Next.js 16 web app. Route handlers under `/api/auth` and `/api/backend` forward signed-in calls to the backend, so the API needs no CORS for the web app |
| `backend` | ASP.NET Core 10 API: EF Core, PostgreSQL, Identity with TOTP MFA, JWT. Runs migrations at start, the daily scoring run, the daily carrier sync, and the tier recommendation runs |
| `ml` | Python scoring service: features, Isolation Forest, cohort z-score, XGBoost with SHAP, Prophet forecasts. Needs a shared secret on every route except `/health`, and reads usage through a read-only database role |
| `postgres` | One database per organisation |
| Caddy | The shared reverse proxy: automatic certificates, the only service that publishes ports |
| `carriermock` | The demo carrier usage feed, running inside the stack. It replays synthetic usage, one more day every night |
| The carrier server | The same mock on its own small server with its own Caddy, so the demo feed has a real https address. Port 443 is open only to the SimGuard server. See [carrier-server.md](carrier-server.md) |

The backend asks the ML service for a risk score per SIM-day. Pages never call
the ML service: they read stored results, so they keep working when it is down.
The full picture, with ports and start order, is in
[deployment.md](deployment.md) and the [root README](../README.md#architecture).

## 2. Repository layout

| Path | What is in it |
| ---- | ------------- |
| `backend/SimGuard.Api` | The API: `Endpoints/`, `Scoring/`, `TierRecommendations/`, `Imports/`, `Feed/` (carrier feed), `Ml/` (ML client), `Data/` (EF context and migrations), `Common/` (authorization, audit, helpers) |
| `backend/SimGuard.Api.Tests` | xUnit tests for the API |
| `backend/SimGuard.CarrierMock`, `backend/SimGuard.CarrierMock.Tests` | The demo carrier and its tests |
| `backend/docs` | API contracts, notes and the `*-smoke.ps1` scripts |
| `backend/scripts` | `import-demo-data.ps1` |
| `frontend` | Next.js app: `src/app` (routes), `src/components`, `src/lib` (`data.js`, `copy.js`, `permissions.js`), `src/hooks`. See [frontend/README.md](../frontend/README.md) |
| `ml-service` | Python service (`app/`, `src/`), `artifacts/`, `contracts/`, `notebooks/`, `tests/`, `docs/`. See [ml-service/README.md](../ml-service/README.md) |
| `deploy` | Compose stack for one organisation, `shared/` (the shared Caddy), `scripts/` (organisation, backup, restore, update, data pack), `carrier/` (the carrier server bundle), `aws/` (deploy roles, Run Command scripts) |
| `database`, `initial-migration.sql` | Reference schema and an exported EF migration script |
| `docs` | Cross service documents, including these guides |
| `.github` | The two deploy workflows and the issue to branch automation |

## 3. Prerequisites and versions

These come from the [root README](../README.md#prerequisites). `dotnet ef`
10.0.13 was found installed while this guide was written.

| Tool | Version | Needed for |
| ---- | ------- | ---------- |
| .NET SDK | 10 | backend |
| Node.js and npm | 24 and 11 | frontend |
| Python | 3.13 (the pinned versions match the saved models) | ML service |
| PostgreSQL | 16 (a container is fine) | backend and ML service |
| Docker with Compose v2 | recent | the full stack and deployment |
| PowerShell | 5.1 or 7 | the smoke scripts in `backend/docs/` |
| `dotnet-ef` tool | matching the SDK | migrations (only when an issue asks for one) |

## 4. Setup and configuration

Nothing secret is committed. Each service has an example file with the keys and
safe placeholders. Copy it and fill it in. Real values go in user-secrets,
environment variables, or git-ignored files.

| Service | Example file | Real values go in |
| ------- | ------------ | ----------------- |
| Whole stack (Docker) | [deploy/.env.example](../deploy/.env.example) | `deploy/.env` (git-ignored, `chmod 600`) |
| Backend (local) | [backend/.env.example](../backend/.env.example) | user-secrets or environment variables |
| ML service (local) | [ml-service/.env.example](../ml-service/.env.example) | environment variables |
| Frontend (local) | [frontend/.env.example](../frontend/.env.example) | `frontend/.env.local` (git-ignored) |
| Carrier mock host | [deploy/carrier/mock.env.example](../deploy/carrier/mock.env.example) | `/srv/carrier/mock.env`, made by `create-mock-env.sh` |

### Environment variable groups

Never write real values in a document or in a commit.

| Group | Keys | Notes |
| ----- | ---- | ----- |
| Database | `ConnectionStrings__DefaultConnection`, `Database__MigrateOnStartup`; in Docker `SIMGUARD_DB_NAME`, `SIMGUARD_DB_OWNER`, `SIMGUARD_DB_OWNER_PASSWORD` | Locally use user-secrets for the connection string |
| ML read only role | `SIMGUARD_ML_USER`, `SIMGUARD_ML_PASSWORD`, `ML_DATABASE_URL` | The `dbgrants` job creates the role after the backend has migrated |
| Sign-in tokens | `Jwt__Key`, in Docker `JWT_KEY` | 32 or more characters |
| Backend to ML | `MlService__BaseUrl`, `MlService__SharedSecret`; ML side `ML_SHARED_SECRET`, `ML_ENVIRONMENT`, `READER` | Both secrets must match. Without `ML_SHARED_SECRET` the ML service starts only when `ML_ENVIRONMENT=Development` |
| First Admin | `SimGuard__Seed__AdminEmail`, `SimGuard__Seed__AdminPassword`, `SimGuard__Seed__RecoverAdmin`; in Docker `SIMGUARD_ADMIN_EMAIL`, `SIMGUARD_ADMIN_PASSWORD`, `SIMGUARD_RECOVER_ADMIN` | Used only while no account exists. Remove the seed password after the first sign-in |
| Email | `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SignInUrl`; in Docker `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `BREVO_SIGN_IN_URL` | See "Email" in [deployment.md](deployment.md) |
| Carrier feed | `CarrierFeed__AllowInsecureHttp` (Docker `CARRIER_FEED_ALLOW_INSECURE_HTTP`), `CARRIER_MOCK_DATE`, `SIMGUARD_MOCK_DATA_DIR`, `SIMGUARD_MOCK_ENV_FILE`; the `CarrierFeed:` section in [carrier-feed.md](carrier-feed.md) | `true` only for the demo mock. `false` with real https carriers |
| Scoring and schedule | `Scoring__Enabled`, `Scoring__DailyRunTimeNairobi`, `Scoring__BatchSize`, `Alerts__*`, `TierRecommendations__*` | Defaults and meaning in [backend/docs/scoring-pipeline.md](../backend/docs/scoring-pipeline.md) |
| CORS (local) | `Cors__AllowedOrigins__0` | Only needed if the browser would call the API directly |
| Frontend | `BACKEND_URL` | Read on the server only. Default `http://localhost:5275` |
| Domain and network | `DOMAIN`, `SIMGUARD_SUBNET`, `ML_PROPHET_N_JOBS`, `COMPOSE_PROJECT_NAME`, `HTTP_PORT`, `HTTPS_PORT`, `CADDY_GLOBAL_OPTIONS` | The last three are for local tests only |
| Model files | `SIMGUARD_ML_ARTIFACTS_DIR`, `ML_ARTIFACT_DIR` | Folder with the model artifacts. The ML service will not start if one is missing |
| ML tuning | `ML_PROPHET_CHECKPOINTS`, `ML_USE_PRECOMPUTED_PROPHET`, `ML_DAILY_USAGE_PATH` | See [ml-service/README.md](../ml-service/README.md) |
| Carrier mock | `CarrierMock__Keys__<Carrier>`, `MOCK_ADMIN_KEY`, `MOCK_START_DAY` and the release time | One key per carrier. See [carrier-server.md](carrier-server.md) |

The explanation of every backend authentication key is in
[backend/docs/auth.md](../backend/docs/auth.md).

### First-time setup

These steps follow the root README and are read only (the installs were not
re-run). Start PostgreSQL first, then:

```bash
# backend
cd backend/SimGuard.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=<your db>;Username=postgres;Password=<your password>"
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>"
dotnet user-secrets set "SimGuard:Seed:AdminEmail" "admin@simguard.local"
dotnet user-secrets set "SimGuard:Seed:AdminPassword" "<temporary password, 12+ characters>"
dotnet ef database update

# ML service
cd ml-service
python -m venv venv
venv/Scripts/python -m pip install -r requirements.txt      # Linux and macOS: venv/bin/python

# frontend
cd frontend
npm install
cp .env.example .env.local
```

Use a database name of your own choice. The shared development database named
`simguard` must never be used for tests, smoke scripts or migrations you are
trying out.

## 5. Run locally

Start the three services in this order, each in its own terminal. The ML
service comes first because the backend checks it, and the backend before the
frontend because the frontend calls it.

```bash
# 1. ML service (needs the artifacts in ml-service/artifacts)
cd ml-service && ML_ENVIRONMENT=Development venv/Scripts/python -m uvicorn app.main:app --port 8001

# 2. Backend, http://localhost:5275
cd backend/SimGuard.Api && dotnet run

# 3. Frontend, http://localhost:3000
cd frontend && npm run dev
```

`python -m uvicorn --help` was checked. The three start commands themselves
were not run.

Sign in with the seeded Admin. Sign-in needs an authenticator app (TOTP). For
scoring against the backend database, start the ML service with
`READER=postgres`, `ML_DATABASE_URL` and `ML_SHARED_SECRET` set, as in
[backend/docs/scoring-pipeline.md](../backend/docs/scoring-pipeline.md). Interactive
ML docs are at `http://localhost:8001/docs` (they need the secret header when
`ML_SHARED_SECRET` is set).

To run the whole stack in containers instead, see [Deployment](#10-deployment).

## 6. The data pipeline

```
data pack --> imports (5 CSV files) --> scoring --> alerts --> review
carrier feed (daily) -----------------^
```

1. **Data pack.** `python deploy/scripts/prepare-data.py` writes `deploy/data-pack/`
   (git-ignored): the numbered import files in import order, a `README.txt`
   with the expected totals, `mock-data/` and `mock.env`. Options: `--out`,
   `--org-name`, `--sim-offset`, `--sim-count`, `--no-sessions`, `--rotate-keys`
   (checked with `--help`). One slice per organisation is explained in
   "Demo data pack" in [deployment.md](deployment.md). For the older sample
   files, `ml-service/scripts/make_import_files.py` writes them into
   `ml-service/data/import_samples/` (read only, see
   [ml-service/README.md](../ml-service/README.md)).
2. **Import order.** Departments, employees, SIMs, allocations, usage. SIMs need
   carriers first (Settings, Carriers). Each import is a preview and then a
   commit, with a 25 MB and 250000 row limit. The contract is
   [backend/docs/imports.md](../backend/docs/imports.md). For a scripted import
   use `backend/scripts/import-demo-data.ps1` (read only: it signs in as a
   Security Officer, previews, stops on any row error, then commits).
3. **Scoring.** A SIM-day is scored only with usage on the 15 days ending that
   day. The ML service returns a probability and a band: `clear` below 0.10,
   `flag` above 0.90, `escalated` between. Results are stored in `risk_scores`
   and repeating a run creates no duplicates.
4. **Alerts.** A sustained pattern raises an alert: at least 3 of the last 5
   scored days not clear, and at least 1 of them `flag`. One open alert per SIM,
   and a 7 day cooldown after a false positive. The settings and the alert type
   rules are in [backend/docs/scoring-pipeline.md](../backend/docs/scoring-pipeline.md).
5. **The 02:00 daily run.** The backend scores every scorable SIM-day not scored
   yet every day at 02:00 Africa/Nairobi (`Scoring:DailyRunTimeNairobi`, on while
   `Scoring:Enabled` is true). Runs are also started by "Refresh scores" and by
   every usage import that inserted or updated rows.
6. **The 01:30 carrier sync.** Every enabled carrier feed is synced once a day at
   01:30 Africa/Nairobi (`CarrierFeed:DailySyncTime`), after the mock releases
   its new day at 01:00 and before the 02:00 scoring run. Keep it before the
   scoring time. Details, catch-up after downtime and the demo controls are in
   [carrier-feed.md](carrier-feed.md).
7. **Tier recommendations.** A separate run shares the same lock and is allowed
   at most every `IntervalDays` (90 to 365). See
   [backend/docs/api-tier-recommendations.md](../backend/docs/api-tier-recommendations.md).

Run one API instance per database: the run lock lives in the process.

## 7. Tests

| Where | Command | Needs a database |
| ----- | ------- | ---------------- |
| Backend | `dotnet test backend/SimGuard.Api.Tests` | Some tests do. They read `SIMGUARD_TEST_CONNECTION` |
| Frontend | `cd frontend && npm test` and `npm run lint` | No |
| ML service | `cd ml-service && venv/Scripts/python -m pytest -q` | No |
| Backend smoke scripts | `backend/docs/*-smoke.ps1` | Yes, plus a running Development API |
| Deploy scripts | `bash deploy/aws/check.sh` | No (needs shellcheck and actionlint, or Docker) |

Results when this guide was written (all checked, on this checkout):

- `npm run lint`: no output, no errors.
- `npm test`: 33 passed.
- `dotnet build backend/SimGuard.Api.Tests`: succeeded, 0 warnings. `dotnet test`
  without `SIMGUARD_TEST_CONNECTION`: 61 passed, 44 skipped (105 total).
- `pytest -q`: 120 passed, 1 skipped.
- `npm run build`: failed here with a `SecurityFatalError`: the installed Next.js
  version was reported by the npm advisory check as affected by a known
  vulnerability. The check needs network access. This guide does not change
  dependencies. Treat a green build as not verified until this is resolved.
- `bash deploy/aws/check.sh`: not run, `shellcheck` and `actionlint` are not
  installed on this machine and the script would otherwise pull Docker images.
  Not verified.

### Database tests

Backend tests that need a database return early when `SIMGUARD_TEST_CONNECTION`
is unset, so they pass without checking anything. That is why 44 were skipped
above. To run them, point the variable at a server and not at the development
database. Each run creates a random database and drops it:

```bash
SIMGUARD_TEST_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=<pw>" dotnet test backend/SimGuard.Api.Tests
```

Not verified: this run needs a database and was not made.

### Smoke scripts

The scripts in `backend/docs/` (`auth`, `alerts`, `imports`, `scoring`, `carriers`,
`carrier-feed`, `recovery`, `roles`, `wiring` and `access-requests`) call a running
Development API. Each script header lists its parameters and the environment
variables it needs, for example `SIMGUARD_SMOKE_ADMIN_EMAIL` and
`SIMGUARD_SMOKE_ADMIN_PASSWORD`. Rules:

- Run them against a throwaway database with a unique name, on its own port.
  They create and change data. Never run them against the development database
  named `simguard`, and never against a live server.
- Sign-in is rate limited to 10 a minute. The alerts smoke script says to start
  the API with `RateLimiting__Auth__PermitLimit=1000` for its run.
- Drop the database and stop the API when you are done.
- Run the script that matches your change. Run the whole set once before a
  merge that changed backend code.

None of the smoke scripts was run while this guide was written (read only).

## 8. Adding a screen or an endpoint

### An endpoint

1. Add or extend a file in `backend/SimGuard.Api/Endpoints/`. Each file has a
   `Map<Area>Endpoints` method that makes a group.
2. Register the group in `Program.cs` (`api.Map<Area>Endpoints();`).
3. Give **every** endpoint an explicit authorization policy. The policy names
   are in `Common/Authorization/AuthPolicies.cs`: `OrgRead`, `OrgWrite`,
   `AdminOnly`, `AuditRead`, `AnyRole` and `SignedIn` (and `MfaEnrollment` for
   enrolment). An anonymous endpoint says `AllowAnonymous()` and, like the access request one, is rate limited.
4. Do not change an existing response shape. Add fields only.
5. Record changes with the audit writer in `Common/AuditWriter.cs`.
6. Update the contract in `backend/docs/` and the matching `*-smoke.ps1`.
7. Add xUnit tests in `backend/SimGuard.Api.Tests`.
8. Do not add an EF migration unless the task says so. If it does, run
   `dotnet ef migrations add <Name>` from `backend/SimGuard.Api` (not verified)
   and apply it only to a throwaway database.
9. If the role rules change, update the role matrix in
   [backend/docs/auth.md](../backend/docs/auth.md) and
   `frontend/src/lib/permissions.js` together.

### A screen

Follow "Adding a screen" in [frontend/README.md](../frontend/README.md):

1. Add `src/app/(app)/<route>/page.jsx` as a thin server component that renders
   one client view in `src/components/<area>/`. Wrap a view that reads
   `useSearchParams` in `<Suspense>`.
2. Add the data functions to `src/lib/data.js`. Screens never call `fetch`
   themselves.
3. Load data with `useResource`. Build from the shared components (`PageHeader`,
   `GlassCard`, `DataTable`, `FormDialog`, `ConfirmDialog` and so on).
4. Put every user-facing string in `src/lib/copy.js`.
5. Add the page and its actions to `src/lib/permissions.js`, and the route to
   `src/components/layout/nav-items.js` if it belongs in the menu.
6. Do not edit `src/components/ui` (generated shadcn components).
7. Next.js here has breaking changes. Read `node_modules/next/dist/docs` before
   routing or caching work (see `frontend/AGENTS.md`).
8. Keep to WCAG 2.2 AA. Run `npm run lint` and `npm test`.

## 9. ML retraining

Use the checklist in
[ml-service/README.md](../ml-service/README.md#retraining-checklist). Do not copy
it. In short, it covers refitting on training SIMs only, re-deriving both gate
thresholds on the validation split, checking the band shares before deploying,
regenerating SHAP and refreshing the artifact versions shown by `/version`. The
evaluation is in [ml-service/docs/evaluation_report.md](../ml-service/docs/evaluation_report.md).

## 10. Deployment

The whole procedure is [deployment.md](deployment.md). This is the map of it.
All of this section is read only: no deploy command was run.

### One organisation

One Ubuntu VM (4 GB RAM and 2 GB swap) runs the shared Caddy once and one stack
per organisation:

```bash
deploy/scripts/edge.sh up
docker compose -f deploy/docker-compose.yml up -d --build
```

Before that: install Docker, point DNS at the VM, copy the model artifacts and
the data pack, fill in `deploy/.env` and run `deploy/scripts/preflight.sh` until
it says `Ready`. The first build takes 20 to 30 minutes on a small VM. Then
sign in as the seeded Admin, remove the seed password from `deploy/.env`,
restart only the backend, set up email, import the data and connect the feed.
Add the backup cron line. Updating by hand is `deploy/scripts/backup.sh` and then
`deploy/scripts/update.sh`.

### Several organisations on one VM

One shared Caddy and one stack per organisation. DNS is required: an `A` record
for the first organisation's domain and a wildcard `A *.<domain>` (or one record
per slug) for the others, because `create-org.sh` does not create DNS records. Scripts in `deploy/scripts/`:
`create-org.sh <slug> <first-admin-email>`, `list-orgs.sh`, `update-all.sh`,
`backup-all.sh`, `remove-org.sh <slug>` and `adopt-org.sh` for a stack that
already runs. Each organisation gets its own folder, secrets, database,
volumes and backups. Read "Several organisations on one VM" and "Isolation
model and its limits" in [deployment.md](deployment.md) first.

### The carrier server

The demo carrier feed has its own small server. Install, data folder, the key
file, start and check, the outage demo and the release pointer are in
[carrier-server.md](carrier-server.md). The feed contract is
[carrier-feed.md](carrier-feed.md). Scripts: `deploy/aws/carrier/setup.sh` and
`deploy/carrier/scripts/` (`build-mock-data.sh`, `create-mock-env.sh`,
`health-check.sh`, `status.sh`, `release-next-day.sh`, `reset-pointer.sh`).
There is no SSH: use Session Manager. The mock files `mock.env`, the data and
the pointer file are never part of a deploy or a rollback.

### GitHub Actions deploys

Two workflows deploy through AWS Systems Manager Run Command. GitHub signs in
to AWS with OIDC, so no AWS keys are stored, and there is no SSH. Setup, once,
is [deploy-checklist.md](deploy-checklist.md).

| | `deploy-carrier` | `deploy-simguard` |
| - | ---------------- | ----------------- |
| Runs on | push to `main` touching the carrier bundle or the mock | push to `main` touching `backend/**`, `frontend/**` or `ml-service/**`, or by hand |
| Environment | `carrier`, no approval | `production`, waits for a required reviewer |
| Script on the server | `deploy/aws/remote/deploy-carrier.sh` | `deploy/aws/remote/deploy-simguard.sh` |

Neither runs on pull requests, and each runs one at a time.

**The production approval.** A run of `deploy-simguard` stops at "Waiting" with
"Review deployments". A reviewer opens the run in GitHub, ticks `production` and
approves. Nothing happens on the server before that. The mock lives under
`backend/`, so a mock only change also asks for approval: reject it if only the
mock changed.

**What it does on the server.** It backs up the database and checks the dump
exists, replaces only `backend/`, `frontend/` and `ml-service/`, builds one
service at a time, restarts backend, ml and frontend, checks the backend and ML
`/health` and the public address, then records the commit. It never runs
`down`, never removes a volume and never touches `deploy/.env` or `mock.env`.

**Where the live commit is recorded.** `/var/lib/simguard-deploy/app-commit`
holds the deployed commit and `/var/lib/simguard-deploy/previous-commit` the one
before it. Because only three folders are restored, `git status` on the VM
shows them as changed. That is expected. Do not run `git pull` or `update.sh` by
hand on the live VM.

**Rollback.** If a check fails, the workflow puts the previous images and source
back and fails the run. Database migrations are not rolled back: a migration
that the old backend cannot work with needs a database restore. To roll back by
hand, revert the change on `main` through a pull request and approve the deploy,
or follow "Rollback" in [deployment.md](deployment.md#rollback) (previous images
are tagged `simguard-rollback/<service>:previous`, and `restore.sh` restores a
backup). The carrier workflow rolls back by itself.

Check the automation scripts locally with `bash deploy/aws/check.sh` (not
verified here).

## 11. Secrets

- Nothing secret is committed. Example files hold placeholders only.
- Backend, locally: user-secrets (`dotnet user-secrets set ...` in
  `backend/SimGuard.Api`) or environment variables.
- Docker: `deploy/.env` (or `deploy/orgs/<slug>.env`), git-ignored and
  `chmod 600`. `preflight.sh` refuses a file other people can read, a
  placeholder value or a short secret. `create-org.sh` generates every secret
  with `openssl` and prints none of them.
- The ML shared secret and the JWT key differ from each other.
- Carrier API keys entered in Settings are encrypted with ASP.NET Data
  Protection. The key ring must persist (the volume `dpkeys` in the stack). If
  it is deleted, saved keys must be entered again.
- `mock.env` is made on the host by `create-mock-env.sh` and never committed or
  copied from elsewhere.
- Never paste a key, password or token into a chat, an issue, a commit or a log.
- Remove `SIMGUARD_ADMIN_PASSWORD` from `deploy/.env` after the first sign-in.
- AWS access for deploys is OIDC with roles named by repository variables. No
  AWS keys are stored in GitHub.
- Scratch secret files for local tests are deleted when the test is done.

## 12. Git workflow

- Work is tracked in GitHub issues. The issue to branch automation
  (`.github/issue-branch.yml`, `.github/workflows/issue-branch.yml`) names the
  branch `feat/<issue number>-<title>`. Work without an issue uses a
  descriptive branch such as `chore/issue-branch-automation`.
- Stay on the branch for your issue. Do not commit to `main`.
- One commit per step of work, with a short conventional message (`feat:`,
  `fix:`, `docs:`, `build:`, `test:`, `chore:`). Stage files by explicit path,
  never `git add .` or `git add -A`. Write the message to a file and commit with
  `git commit -F <file>`.
- Open a pull request. Merge it with a merge commit
  (`Merge pull request #<n> from ...`), never squash, so every commit stays in
  history.
- Labels group issues and pull requests by area: `frontend`, `backend`,
  `machine-learning`, `data`, `deployment`, `docs`, `testing`, `auth` and
  `carrier-feed`.
- Releases are tags. The tags in this repository are `v0.1.0-models`,
  `v0.2.0-platform`, `v0.3.0-deployment`, `v0.4.0-multi-org` and `v1.0.0`. Make
  the next one as an annotated tag on `main` after the pull request is merged
  (`git tag -a v1.0.1 -m "<what changed>"`, not verified), and list the change in
  [CHANGELOG.md](../CHANGELOG.md).
- Do not push or merge for someone else. A merge to `main` that touches
  `backend/`, `frontend/` or `ml-service/` starts a production deploy that waits
  for an approval.

## 13. Known limitations

Copied from the [root README](../README.md#known-limitations):

- Carrier feeds are mocked. The demo carrier mock replays synthetic usage,
  one more day every night, from its own server over https
  (`https://carrier.sim-guard.app`, [carrier-server.md](carrier-server.md))
  or over plain http on the private network; real carriers need their own
  adapters and https (`CARRIER_FEED_ALLOW_INSECURE_HTTP=false`).
- The models are trained on synthetic call data records, not real traffic.
  Results show the pipeline works, not how it performs on a real fleet.

More notes on the ML side are in
[ml-service/README.md](../ml-service/README.md#known-limitations). Others found
while writing this guide: the backend cannot run as two instances on one
database, the live VM keeps the older single-VM layout of `deploy/`, and
database migrations are not rolled back by a failed deploy.
