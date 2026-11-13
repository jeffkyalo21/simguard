# Carrier server

The demo carrier feed (the mock in `backend/SimGuard.CarrierMock`) runs on its
own small EC2 instance, separate from SimGuard. SimGuard pulls usage from it
over https, the way it would from a real carrier, and the feed can be stopped
to show an outage without touching SimGuard.

| Item | Value |
| ---- | ----- |
| Instance | Ubuntu 24.04, t3.micro (1 GiB), 10 GB disk, instance id in the repo variable `CARRIER_INSTANCE_ID` |
| Instance role | `AmazonSSMManagedInstanceCore` (Session Manager and Run Command, no SSH) |
| Address | an Elastic IP |
| DNS | `carrier.sim-guard.app`, A record to the Elastic IP |
| Bundle | `deploy/carrier/` (compose project `simguard-carrier`: `mock` and `caddy`) |
| Clone | `/opt/simguard-carrier` (read only, updated by the deploy workflow) |
| Host data | `/srv/carrier/mock-data` (CSV files), `/srv/carrier/mock.env` (keys, mode 600), `/srv/carrier/state/release-state.json` (the release pointer) |
| Deploys | `.github/workflows/deploy-carrier.yml`, see [deployment.md](deployment.md#automatic-deploys-from-github) |

Setting it up from nothing, with every command, is
[deploy-checklist.md](deploy-checklist.md). This page explains the pieces.

## Security group

| Direction | Port | Source | Why |
| --------- | ---- | ------ | --- |
| in | 80 | `0.0.0.0/0` | Let's Encrypt HTTP-01 challenge; Caddy redirects everything else to https |
| in | 443 | `3.78.174.212/32` (the SimGuard server) only | the feed itself |
| in | 22 | none | no SSH; use Session Manager |
| out | all | `0.0.0.0/0` | packages, Docker images, GitHub, Let's Encrypt, Systems Manager |

Because 443 is closed to everyone else, `curl https://carrier.sim-guard.app/`
from a laptop times out: that is expected. Check it from the SimGuard server
or from the carrier itself with `--resolve` (below). Caddy gets its certificate
through port 80, so the certificate works although 443 is closed to the world.

## What runs

- `mock`: the carrier API, image built on the host from
  `backend/Dockerfile.carriermock`, memory limit 256 MiB. Listens on
  `127.0.0.1:8081` on the host (for the host scripts) and on the compose network.
- `caddy`: `caddy:2`, memory limit 128 MiB, ports 80 and 443, automatic https
  for `carrier.sim-guard.app`. It answers `/admin` and `/admin/*` with 404, so
  the demo controls are reachable from the host only (127.0.0.1:8081).

The mock reads `/srv/carrier/mock.env` (keys and settings), the data folder
read only, and writes only the pointer file. A rebuild or redeploy keeps all
three; nothing in the repository or the workflows writes them.

## Install

On a new instance, as root through Session Manager, `deploy/aws/carrier/setup.sh`
installs Docker with the compose plugin, git, git-lfs and python3, clones the
repository to `/opt/simguard-carrier` and creates the folders. It adds a 1 GB
swap file only on a machine with less than 768 MiB of memory (see Memory).

### Data folder

```bash
sudo /opt/simguard-carrier/deploy/carrier/scripts/build-mock-data.sh
# Mock data in /srv/carrier/mock-data: 700 SIMs, 63000 usage rows, 90 days.
```

It builds the "main" slice of the demo data pack (SIMs 0 to 699, the slice the
SimGuard organisation imported, 2025-01-01 to 2025-03-31) on the host from the
public repository, so nothing is copied from another machine. The per-session
file is left out: SimGuard calls only the daily summary.

### mock.env

```bash
sudo /opt/simguard-carrier/deploy/carrier/scripts/create-mock-env.sh
```

New random keys, one per carrier in the data (Safaricom, Airtel, Telkom) and
`MOCK_ADMIN_KEY`, mode 600, owner root. It prints no key and never overwrites
an existing file. Never copy a mock.env from the SimGuard server, a laptop or
the repository. To type a carrier key into SimGuard, read just that one line:

```bash
sudo grep '^CarrierMock__Keys__Safaricom=' /srv/carrier/mock.env | cut -d= -f2
```

`deploy/carrier/mock.env.example` lists every setting.

### Start and check

The deploy workflow starts it. By hand (the same command the workflow runs):

```bash
cd /opt/simguard-carrier
sudo docker compose -p simguard-carrier -f deploy/carrier/docker-compose.yml up -d --build
deploy/carrier/scripts/health-check.sh
# OK    mock: {"name":"SimGuard carrier mock","sims":700,"carriers":[...],"status":"releasing"}
# OK    https://carrier.sim-guard.app/ through Caddy with a valid certificate
```

From the SimGuard server (the only address 443 is open to):

```bash
curl -sS https://carrier.sim-guard.app/
# {"name":"SimGuard carrier mock","sims":700,...}    no key needed for this page
curl -sS -o /dev/null -w '%{http_code}\n' "https://carrier.sim-guard.app/v1/usage/summary?date=2025-01-01"
# 401                                                                 the feed needs a key
```

## Stopping it for an outage demo

```bash
sudo docker compose -p simguard-carrier -f /opt/simguard-carrier/deploy/carrier/docker-compose.yml stop mock
# SimGuard: Sync now fails with a carrier error (Caddy answers 502), the feed shows the failure
sudo docker compose -p simguard-carrier -f /opt/simguard-carrier/deploy/carrier/docker-compose.yml start mock
# Sync now works again and catches up the days it missed
```

Stop only `mock`, so Caddy keeps its certificate and answers at once. Never use
`down -v`: it would delete the certificate volume. The pointer file survives a
stop; nights that pass while it is stopped are handled as described in
[carrier-feed.md](carrier-feed.md#nightly-release).

## Release pointer and demo controls

The mock releases one more day of data every night at 01:00 Nairobi time and
starts with 80 of the 90 days released. Settings, the pointer, the end of the
data and the date shift are in
[carrier-feed.md](carrier-feed.md#nightly-release). The host scripts (run
with sudo, they read the admin key from mock.env and print none):

| Script | Does |
| ------ | ---- |
| `deploy/carrier/scripts/status.sh` | released days, newest released date, next release (Nairobi and UTC), days left |
| `deploy/carrier/scripts/release-next-day.sh` | releases one more day now; tonight's release still happens |
| `deploy/carrier/scripts/reset-pointer.sh [day]` | back to `MOCK_START_DAY`, or to the given day |
| `deploy/carrier/scripts/health-check.sh` | mock on loopback, Caddy and its certificate |

## Memory

Measured with Docker on a laptop (Linux containers, cgroup v2), with the
700-SIM data folder and no sessions file:

| What | Measured |
| ---- | -------- |
| mock, idle after start | 79 MiB |
| mock, 720 summary requests, 8 at a time | 115 MiB (`docker stats`), peak 118 MiB (`memory.peak`), 113 MiB after |
| image build (`dotnet publish` in the SDK image), 2 CPUs | passes with a 200 MiB limit, OOM-killed at 160 MiB; 216 to 225 MiB used when allowed more |
| mock image size | 342 MB |
| Caddy | about 13 to 15 MiB (earlier measurement in [deployment.md](deployment.md#memory), not repeated) |

Not measured: Ubuntu, the SSM agent and the Docker daemon on the instance
itself; 250 to 350 MiB is the usual figure for a minimal Ubuntu 24.04 with them.

**t3.micro (1 GiB)**: about 350 MiB (system) + 120 (mock) + 15 (Caddy) = about
485 MiB while running, and about 600 MiB during a build (the running mock stays
up while the new image builds). Fits without swap; setup.sh adds none.

**t3.nano (0.5 GiB, 458 MiB usable)**: running fits only just (about 485 MiB
against 458 usable, so a little is swapped), and a build does not fit without
swap. With the 1 GB swap file (setup.sh adds it on a machine under 768 MiB)
it works, with slower builds. The container limits (256 MiB mock, 128 MiB
Caddy) stay the same on both.

## Why the mock is still in the main stack

The task was to remove the mock from `deploy/docker-compose.yml` only if
nothing depends on it. Several things do, so it stays for now:

- `deploy/scripts/create-org.sh` builds each organisation's mock data and mock.env
- `deploy/scripts/update.sh` builds `carriermock` and waits for it to be healthy
- `deploy/scripts/preflight.sh` checks the mock data folder and env file
- `deploy/scripts/prove-orgs.py` checks that each organisation's mock is isolated
- `deploy/scripts/remove-org.sh` and docs/deployment.md (several organisations)
- the live SimGuard VM runs the old layout (commit f296146) with its own `carriermock`

Once SimGuard points at `https://carrier.sim-guard.app`, the in-stack mock is
unused but harmless (internal network only, 79 MiB). Removing it is a separate
change to those scripts.
