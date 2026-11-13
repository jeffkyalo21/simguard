# SimGuard

SimGuard manages corporate SIM cards and watches their usage for misuse.
Companies that hand SIMs to staff lose money to lost devices, SIMs used in
other devices, data abuse, and plans billed but not used. SimGuard keeps the
inventory (SIMs, employees, departments, allocations), ingests daily usage from
carriers, scores every SIM every day with a machine learning pipeline, raises
alerts for a Security Officer to investigate, recommends a better data tier
when a SIM is over or under provisioned, and keeps an audit trail of every
change.

Academic capstone project, Strathmore University. See [LICENSE.md](LICENSE.md).

## Architecture

<img width="940" height="627" alt="image" src="https://github.com/user-attachments/assets/34b19660-f41c-4552-8c99-050f47b34760" />


Everything right of Caddy is one organisation's stack; several organisations can
share one VM and one Caddy. Only Caddy is public. The browser never calls the API: the Next.js server does,
on its behalf, over a private network. The backend asks the ML service for a
risk score per SIM-day with a shared secret; the ML service reads usage through
a read-only database role.

## Modules

| Folder | What it is |
| ------ | ---------- |
| `backend/` | ASP.NET Core 10 API (EF Core, PostgreSQL, Identity with TOTP MFA, JWT). Also the carrier mock and the xUnit tests. See [backend/README.md](backend/README.md) |
| `frontend/` | Next.js 16 web app: dashboard, SIMs, allocations, usage, alerts, tier recommendations, imports, audit log, admin. See [frontend/README.md](frontend/README.md) |
| `ml-service/` | Python scoring pipeline (features, Isolation Forest, cohort z-score, XGBoost with SHAP, Prophet forecasts) behind a FastAPI service, plus notebooks and the evaluation. See [ml-service/README.md](ml-service/README.md) |
| `deploy/carrier/` | The demo carrier feed bundle for its own server (mock and Caddy), host scripts |
| `deploy/aws/` | GitHub deploy roles (trust and permission policies), Run Command scripts, carrier server setup |
| `deploy/` | Docker Compose stack for one organisation, the shared Caddy, organisation, backup, restore and update scripts, `.env.example` |
| `database/`, `initial-migration.sql` | Reference schema and an exported EF migration script |
| `docs/` | Cross service docs: [deployment](docs/deployment.md), [carrier feed](docs/carrier-feed.md), [carrier server](docs/carrier-server.md), [deploy checklist](docs/deploy-checklist.md) |

## Prerequisites

| Tool | Version | Needed for |
| ---- | ------- | ---------- |
| .NET SDK | 10 | backend |
| Node.js and npm | 24 and 11 | frontend |
| Python | 3.13 | ML service (the pinned versions match the saved models) |
| PostgreSQL | 16 | backend and ML service (a container is fine) |
| Docker with Compose v2 | recent | the full stack and deployment |
| PowerShell | 5.1 or 7 | the smoke scripts in `backend/docs/` |

## Setup

Run these from the repository root. Start PostgreSQL first, for example:

```bash
docker run -d --name simguard-pg -e POSTGRES_PASSWORD=<choose one> -e POSTGRES_DB=simguard -p 5432:5432 postgres:16
```

Backend (see `backend/.env.example` for every key):

```bash
cd backend/SimGuard.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=simguard;Username=postgres;Password=<your password>"
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>"
dotnet user-secrets set "SimGuard:Seed:AdminEmail" "admin@simguard.local"
dotnet user-secrets set "SimGuard:Seed:AdminPassword" "<temporary password, 12+ characters>"
dotnet ef database update
```

ML service:

```bash
cd ml-service
python -m venv venv
venv/Scripts/python -m pip install -r requirements.txt      # Linux and macOS: venv/bin/python
```

Frontend:

```bash
cd frontend
npm install
cp .env.example .env.local
```

## Configuration

Nothing secret is committed. Each service has an example file with the keys and
safe placeholders; copy it and fill it in.

| Service | Example file | Real values go in |
| ------- | ------------ | ----------------- |
| Whole stack (Docker) | [deploy/.env.example](deploy/.env.example) | `deploy/.env` (git-ignored, `chmod 600`) |
| Backend (local) | [backend/.env.example](backend/.env.example) | user-secrets or environment variables |
| ML service (local) | [ml-service/.env.example](ml-service/.env.example) | environment variables |
| Frontend (local) | [frontend/.env.example](frontend/.env.example) | `frontend/.env.local` (git-ignored) |

Variables in `deploy/.env.example`:

| Variable | Meaning |
| -------- | ------- |
| `DOMAIN` | Public hostname; its DNS record must point at the VM |
| `SIMGUARD_DB_NAME`, `SIMGUARD_DB_OWNER`, `SIMGUARD_DB_OWNER_PASSWORD` | Database name, owner role and its password |
| `SIMGUARD_ML_USER`, `SIMGUARD_ML_PASSWORD` | Read-only database role the ML service uses |
| `JWT_KEY` | Signs sign-in tokens (32+ characters) |
| `ML_SHARED_SECRET` | Secret the backend sends to the ML service (32+ characters) |
| `SIMGUARD_ML_ARTIFACTS_DIR` | Folder on the VM with the model artifacts |
| `SIMGUARD_MOCK_DATA_DIR`, `SIMGUARD_MOCK_ENV_FILE` | Carrier mock data folder and its key file |
| `SIMGUARD_ADMIN_EMAIL`, `SIMGUARD_ADMIN_PASSWORD` | First Admin, used only while no account exists |
| `SIMGUARD_RECOVER_ADMIN` | Emergency Admin recovery switch, `false` normally |
| `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `BREVO_SIGN_IN_URL` | Invitation and recovery email |
| `CARRIER_FEED_ALLOW_INSECURE_HTTP` | `true` only for the demo mock; `false` with real https carriers |
| `CARRIER_MOCK_DATE` | Demo feed "today": a date or `last` |
| `SIMGUARD_SUBNET`, `ML_PROPHET_N_JOBS` | Private subnet and forecast workers |
| `COMPOSE_PROJECT_NAME`, `HTTP_PORT`, `HTTPS_PORT`, `CADDY_GLOBAL_OPTIONS` | Optional, local tests only |

## Run locally

Three terminals, in this order:

```bash
# 1. ML service (needs the artifacts in ml-service/artifacts)
cd ml-service && ML_ENVIRONMENT=Development venv/Scripts/python -m uvicorn app.main:app --port 8001

# 2. Backend, http://localhost:5275
cd backend/SimGuard.Api && dotnet run

# 3. Frontend, http://localhost:3000
cd frontend && npm run dev
```

Sign in with the seeded Admin. Sign-in needs an authenticator app (TOTP). For
scoring against the backend database, start the ML service with `READER=postgres`
as in [backend/docs/scoring-pipeline.md](backend/docs/scoring-pipeline.md).

To run the whole stack in containers instead, see Deploy below.

## Tests and smoke scripts

| Where | Command |
| ----- | ------- |
| Backend unit and integration tests | `SIMGUARD_TEST_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=<pw>" dotnet test backend/SimGuard.Api.Tests` (tests that need a database return early, so they pass without checking anything, when the variable is unset; each run uses a random database and drops it) |
| ML service | `cd ml-service && venv/Scripts/python -m pytest -q` |
| Frontend | `cd frontend && npm test` and `npm run lint` |
| Backend smoke scripts | `backend/docs/*-smoke.ps1`, run against a running Development API on a throwaway database. Each script's header lists its parameters and the environment variables it needs (for example `SIMGUARD_SMOKE_ADMIN_EMAIL` and `SIMGUARD_SMOKE_ADMIN_PASSWORD`). The contract for each area is in `backend/docs/` |

Never run the smoke scripts against a database you care about: they create and
change data.

## Deploy

One Ubuntu VM runs the whole system: the shared Caddy once per VM, then one
stack per organisation.

```bash
deploy/scripts/edge.sh up
docker compose -f deploy/docker-compose.yml up -d --build
```

The full procedure (fresh VM, DNS, secrets, first sign-in, email, backups,
updates, recovery, and running several organisations with
`deploy/scripts/create-org.sh`) is in [docs/deployment.md](docs/deployment.md).

Pushes to `main` deploy automatically through GitHub Actions and AWS Systems
Manager (no SSH): `deploy-carrier` for the carrier server, `deploy-simguard`
(after an approval) for the application. Setup: [docs/deploy-checklist.md](docs/deploy-checklist.md).

## Roles

| Role | What it can do |
| ---- | -------------- |
| Admin | The operator: creates and manages user accounts, reads the system audit trail. Cannot see or change the organisation's data |
| SecurityOfficer | Manages the organisation: departments, employees, SIMs, carriers, allocations, tiers, alert reviews |
| Auditor | Read only: every organisation read, the audit log and the auth event log |

The endpoint by role matrix is in [backend/docs/auth.md](backend/docs/auth.md).

## Workflow

- Work is tracked in GitHub issues. The issue to branch automation
  (`.github/issue-branch.yml`) names the branch `feat/<number>-<title>`.
  Work without an issue uses a descriptive branch such as `feat/deploy-prep`,
  `feat/multi-org-stacks` or `chore/issue-branch-automation`.
- After the first three setup commits, nothing was committed straight to
  `main`. Every branch is merged through a pull request with a merge commit
  (`Merge pull request #<n> from ...`), never squashed, so each commit of a
  branch stays in history. Some branches also merged `main` into themselves.
- One commit per step of work, with a short conventional message (`feat:`,
  `fix:`, `docs:`, `build:`, `test:`, `chore:`), files staged by explicit path.
  Commits are authored by the developer and carry no co-author, sign-off or
  tool trailers.
- Labels group issues and pull requests by area: `frontend`, `backend`,
  `machine-learning`, `data`, `deployment`, `docs`, `testing`, `auth` and
  `carrier-feed`.
- Milestones are listed in [CHANGELOG.md](CHANGELOG.md).

## Known limitations

- Carrier feeds are mocked. The demo carrier mock replays synthetic usage,
  one more day every night, from its own server over https
  (`https://carrier.sim-guard.app`, [docs/carrier-server.md](docs/carrier-server.md))
  or over plain http on the private network; real carriers need their own
  adapters and https (`CARRIER_FEED_ALLOW_INSECURE_HTTP=false`).
- The models are trained on synthetic call data records, not real traffic.
  Results show the pipeline works, not how it performs on a real fleet.
