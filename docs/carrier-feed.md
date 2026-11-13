# Carrier usage feed

Each carrier in Settings can be connected to a usage feed. SimGuard pulls
daily usage from it automatically, matches it to your SIMs and starts scoring.
Real carriers are not connected yet: a small mock service stands in for them.

```
Settings > Carriers > Usage feed
        |  save / test / sync now          daily background service
        v                                          |
SimGuard API  ---- GET /v1/usage/summary --->  carrier feed (mock today)
        |             X-Api-Key
        v
daily_usage  ->  import history ("carrier feed")  ->  scoring run (trigger "import")
```

## The feed contract

This is what a carrier must offer (the mock implements it). Everything is
JSON over HTTPS.

**Authentication.** Every request carries `X-Api-Key: <key>`. A missing or
wrong key is `401`. Each carrier has its own key and sees only its own SIMs.

### `GET /v1/usage/summary?date=YYYY-MM-DD&page=1&pageSize=500`

One row per ICCID for one day, paged (`pageSize` 1 to 1000, default 500).

```json
{
  "date": "2025-02-01",
  "page": 1,
  "pageSize": 500,
  "total": 334,
  "items": [
    {
      "iccid": "8925400000000000006",
      "date": "2025-02-01",
      "dataMb": 491.13,
      "voiceMinutes": 3.867,
      "sms": 4,
      "roamingMb": 0,
      "billedMb": 491.13,
      "downloadMb": 263.72,
      "uploadMb": 227.41,
      "dataSessionCount": 2,
      "dataDurationS": 478,
      "hotspotSessions": 0,
      "publicApnSessions": 0,
      "uniqueTowers": 1,
      "geoFlagCount": 0,
      "peakMb": 184.76,
      "offpeakMb": 306.37,
      "roamingSessions": 0,
      "tier": "professional",
      "voiceMinutesRemaining": 196.13,
      "smsRemaining": 131
    }
  ]
}
```

The first five fields (`dataMb`, `voiceMinutes`, `sms`, `roamingMb`,
`billedMb`) are the summary. The rest are the other columns of the daily usage
contract that scoring reads (`ml-service/docs/daily_usage_schema.md`). SimGuard
needs `iccid` and `dataMb`; any other field that is missing is stored as 0
(`downloadMb` and `billedMb` default to `dataMb`, `tier` to the SIM's tier).
The more of them a carrier supplies, the better the scores. `roamingMb` is
shown for completeness; SimGuard stores `roamingSessions` and `billedMb`.

A date the carrier has not published yet must be `404` with the body
`{ "code": "date_not_available", "message": "..." }`. SimGuard treats exactly
that as "caught up". Any other `404` is reported as a wrong address.

### `GET /v1/usage/sessions?iccid=&date=&page=&pageSize=`

Per-session rows for one SIM and day (`sessionId`, `startedAt`, `durationS`,
`type`, `downloadMb`, `uploadMb`, `apn`, `hotspot`, `cellTowerId`,
`callDestination`, `roaming`), paged like the summary. SimGuard does not call
it yet; the mock serves it so the drill-down can be built later. An ICCID of
another carrier returns an empty page.

### Errors SimGuard handles

| Answer | SimGuard does |
| ------ | ------------- |
| `401` | stops at once: "The carrier rejected the API key (401)." The cursor does not move. |
| `429` | waits `Retry-After` (or backs off 2, 4, 8 seconds), retries up to 4 times, then fails with the reason. |
| `5xx`, timeout, no connection | same retries, then fails with the reason. |
| `404 date_not_available` | the run ends successfully: caught up. |
| other `4xx` | fails with the status. |
| a day with no rows | the run ends without moving the cursor; the day is tried again next run. |

## The mock carrier service

Project: `backend/SimGuard.CarrierMock` (separate from the API, no database).
It reads the demo data from files and serves it with the carrier assignment of
`backend/scripts/import-demo-data.ps1`:

| File (in `CarrierMock:DataDir`) | Used for |
| ------------------------------- | -------- |
| `sims.csv` | ICCID and carrier of each SIM (each carrier gets its own slice) |
| `usage.csv` | the daily usage rows |
| `sim_mapping.csv` (optional) | `sim_id` to ICCID, for sessions |
| `../synthetic_cdr_sessions.csv` (optional, `CarrierMock:SessionsFile`) | session rows |

The default folder is `ml-service/data/import_samples`. Create the files first:
`venv/Scripts/python scripts/make_import_files.py` from `ml-service/`.

### Run it

```powershell
$env:CarrierMock__Keys__Safaricom = '<a key>'
$env:CarrierMock__Keys__Airtel    = '<another key>'
$env:CarrierMock__Keys__Telkom    = '<another key>'
dotnet run --project backend/SimGuard.CarrierMock --urls http://127.0.0.1:5391
```

A carrier without a configured key gets a random one, printed once at start.
Other settings: `CarrierMock__DataDir`, `CarrierMock__SessionsFile`, and the
release settings below. `CarrierMock__StartDate` still works (`last` releases
every day, a date releases the days up to it) but `MOCK_START_DAY` replaces it.

In Settings, Carriers, Usage feed, use the address `http://127.0.0.1:5391`
(plain http is accepted for localhost only) and the key of that carrier. The
hosted demo feed is `https://carrier.sim-guard.app` ([carrier-server.md](carrier-server.md)).

### Nightly release

The mock does not serve all of its data at once. A pointer,
`released_through_day`, says how many days of the data are released: day 1 is
the first date in `usage.csv`. A date after the pointer answers `404`
`date_not_available`, exactly what SimGuard treats as "the carrier has no data
for that day yet". Every night at `MOCK_RELEASE_TIME` in `MOCK_TIMEZONE` the
pointer moves on by one day.

| Setting (environment, mock.env) | Default | Meaning |
| ------------------------------- | ------- | ------- |
| `MOCK_START_DAY` | `80` (capped at the number of days) | days released at the very first start (80 of 90: 2025-01-01 to 2025-03-21) |
| `MOCK_RELEASE_TIME` | `01:00` | local time of the nightly release, `HH:mm` |
| `MOCK_TIMEZONE` | `Africa/Nairobi` | IANA zone of the release time |
| `MOCK_DATE_SHIFT_DAYS` | `0` | moves every served date forward by this many days; a multiple of 7, see Date shift |
| `MOCK_STATE_FILE` | `<data folder>/release-state.json` | where the pointer is kept; `/srv/carrier/state/release-state.json` on the carrier server |
| `MOCK_SCHEDULER_ENABLED` | `true` | `false` stops the nightly release (manual releases still work) |
| `MOCK_ADMIN_KEY` | none | key for the admin routes; without it any carrier key is accepted there |

A setting with a wrong value stops the mock at start with a message naming it.

**Persistence.** The pointer, the last nightly release and whether the end of
the data was logged are written to the state file after every change (to a
temporary file, then renamed). A restart or redeploy continues from it;
`MOCK_START_DAY` is used only when there is no state file yet.

**Missed nights.** If the mock was stopped over one or more release times, the
next start releases one day (for the most recent night), logs a warning with
the number of nights skipped, and continues with one day per night. It never
releases more than one day per night and never catches up several days at
once. The very first start releases nothing extra: it starts at
`MOCK_START_DAY` and waits for the next release time.

**End of the data.** When the last day is released the status becomes
`no more days`. The pointer stays on the last day (no loop back to day 1), a
later date answers `404` with the message "no more days, the data ends at ...",
and the mock logs "No more days ..." once (remembered in the state file).
`reset-pointer` starts it again.

**Date shift.** The demo data covers 2025-01-01 to 2025-03-31.
`MOCK_DATE_SHIFT_DAYS=N` serves day D as D + N: in the `date` query, the `date`
field of the rows and the session start times; the data itself is unchanged.
A multiple of 7 keeps the weekday of every row, which the weekend features of
the model depend on. `GET /admin/status` shows `suggestedDateShiftDays`, the
multiple of 7 that makes the newest released day yesterday.
Effect on SimGuard, checked against the importer and the scorer (not run end
to end): the shifted days import like any others, but SimGuard already holds
the unshifted history (2025-01-01 to 2025-03-21), so the first shifted day
starts a new run of consecutive days. Scoring needs 15 consecutive days per SIM
(`MlLimits.ScoreLookbackDays`), so the first 14 shifted days are imported but
not scored, unless the feed start date in Settings is set to the shifted first
day so the sync backfills the whole shifted history (at most 31 days per run,
so three runs for 80 days). The sync also stops at today in UTC, so a shift
that pushes released days into the future hides them until that date. Leave
the shift at 0 unless the demo needs current dates.

### Demo controls

Admin routes need the header `X-Admin-Key: <MOCK_ADMIN_KEY>` (or, when
`MOCK_ADMIN_KEY` is not set, `X-Api-Key` with any carrier key); otherwise `401`
`invalid_admin_key`. On the carrier server Caddy blocks `/admin` from outside,
so use the host scripts in `deploy/carrier/scripts/` ([carrier-server.md](carrier-server.md#release-pointer-and-demo-controls)).

| Call | Does |
| ---- | ---- |
| `GET /admin/status` | status (`releasing` or `no more days`), released days and dates, days left, next release (local and UTC), shift |
| `POST /admin/release-next-day` | releases one more day now; the nightly schedule is unchanged |
| `POST /admin/reset-pointer` `{ "day": 85 }` | sets the pointer (no body: back to `MOCK_START_DAY`) |
| `POST /admin/advance-day` | same as `release-next-day` (older name) |
| `POST /admin/set-date` `{ "date": "2025-02-10" }` | releases the days up to that data date |
| `POST /admin/fault` `{ "fault": "rate_limit", "count": 2 }` | turns a fault on |
| `POST /admin/reset` | turns every fault off |
| `GET /admin/state` | release status and faults |

Faults are off by default: `slow` (`delayMs`, default 3000), `server_error`
(500), `empty_day` (summary has no rows), `rate_limit` (429 with
`Retry-After: 1`). `count` makes a fault switch itself off after that many
requests, so a client that retries recovers; leave it out to keep the fault on
until `reset`.

### Demo script

On the carrier server (Session Manager), then in SimGuard as a SecurityOfficer:

1. `sudo deploy/carrier/scripts/status.sh`: note `releasedThroughDate` (2025-03-21 after the first start) and `nextReleaseAt`.
2. SimGuard, Settings, Carriers, Usage feed: Sync now. Expected: "Caught up: the carrier has no data for 2025-03-22 yet." (nothing imported, nothing scored).
3. `sudo deploy/carrier/scripts/release-next-day.sh`: `"status": "released"`, `releasedThroughDate` one day later.
4. Sync now again: one day imported; scoring for that day is queued at once (trigger `import`), so new alerts can appear within minutes.
5. Outage: stop the mock ([carrier-server.md](carrier-server.md#stopping-it-for-an-outage-demo)); Sync now shows the failure; start it again; Sync now recovers.
6. After the demo, only to replay the same days: `sudo deploy/carrier/scripts/reset-pointer.sh` (SimGuard keeps the imported days; re-imported rows are upserted, never doubled).

### Release time and SimGuard's schedule

Checked against `CarrierFeedWorker`, `CarrierFeedSync` and `ScoringScheduler`:

- The mock releases a day at 01:00 Nairobi time.
- SimGuard's feed sync is not tied to a clock time: it runs 60 seconds after
  the API starts (`CarrierFeed:StartupDelaySeconds`) and then every 24 hours
  (`CarrierFeed:IntervalMinutes` 1440). A day released at 01:00 is pulled at
  the next sync, up to about 24 hours later depending on when the API started.
- The daily scoring run at 02:00 Nairobi (`Scoring:DailyRunTimeNairobi`) does
  not sync. A synced day does not wait for it: the sync itself queues scoring
  (trigger `import`) for the SIMs and days it imported. So nothing is missed,
  only delayed until the next sync.
- Smallest fix (proposed, not made): a fixed daily sync time for the feed
  worker, `CarrierFeed:DailyRunTimeNairobi` = `01:30`, computed with the same
  `NairobiClock` as the scoring scheduler, instead of the start-time based
  interval. Each released day would then be imported and scored about 30
  minutes after release, before the 02:00 run. Until then, Sync now after a
  release does the same by hand.
- What Sync now does today:
  - **No new day** (the next date is after the pointer): the carrier answers
    `404` `date_not_available`; the run ends as succeeded with "Caught up: the
    carrier has no data for <date> yet."; no import history row, no scoring
    queued, and the last synced date stays where it was.
  - **The same day twice**: does not happen by itself, because a sync resumes
    at the day after the last synced date. If the start date is moved back so
    a day is pulled again, its rows are upserted (counted as unchanged when
    equal, never duplicated) and scoring is queued for that day again.

## SimGuard side

### Settings

On `GET /api/carriers` each carrier has additive fields: `feedBaseUrl`,
`feedEnabled`, `feedKeySet`, `feedStartDate`, `lastSyncDate`, `lastSyncStatus`
(`running`, `succeeded`, `failed`), `lastSyncAt`, `lastSyncMessage`.

| Endpoint | Policy | Does |
| -------- | ------ | ---- |
| `PUT /api/carriers/{id}/feed` | `OrgWrite` | saves `baseUrl`, `enabled`, `startDate` and optionally `apiKey` (left out: keep the saved key). Returns the carrier. 400 on a bad address, key or date. |
| `POST /api/carriers/{id}/feed/test` | `OrgWrite` | calls the summary for the last synced day (or the start day, or yesterday). Optional body `baseUrl`, `apiKey` tries unsaved values. Always 200 when the call was made: `{ ok, message, date }`. |
| `POST /api/carriers/{id}/feed/sync` | `OrgWrite` | starts a run in the background: 202 with the carrier (`lastSyncStatus: running`). 409 when one is running, 400 when no address or key is saved. |

Only the Security Officer can call them; the Auditor reads the status and gets
403 on the three writes.

**The key is write-only.** It is encrypted with ASP.NET Data Protection and
stored in `carrier_secrets`, behind `ICarrierSecretStore` (so AWS SSM
Parameter Store can replace it by registering another implementation). No
response, audit row or log line contains it; the API returns only
`feedKeySet`. The HTTP client does not follow redirects and logs no header
values. In production, persist and share the Data Protection key ring
(`DataProtection:KeysPath`, or your platform's store); without it a saved key
cannot be read after a restart or on a second instance; the sync then fails
with "The feed is not set up" until the key is saved again.

The address must be `https://` (no user info or query string); `http://` is
accepted only for loopback, or everywhere with `CarrierFeed:AllowInsecureHttp`.

### The importer

- Resumes the day after `lastSyncDate` per carrier. With no cursor it starts
  at `feedStartDate`, else 7 days ago. It goes day by day up to today (UTC),
  at most `MaxDaysPerRun` (31) days per run.
- Each day is saved in one transaction together with the cursor. A failure on
  day N leaves days before N imported and the cursor on the last good day.
- Rows are matched to the carrier's SIMs by ICCID. Unknown ICCIDs, and ICCIDs
  of SIMs that belong to another carrier, are counted as rejected, not as
  errors. Rows with no `dataMb`, a negative value, another date than asked, or
  no usable tier are rejected too.
- Upserts by (SIM, date): running a day twice adds no rows and changes nothing.
  Choosing a start date at or before the last synced day makes the next sync
  pull again from that day (a backfill or re-pull).
- One run per carrier at a time, across Sync now and the daily service.
- After rows are imported, a scoring run is queued (trigger `import`) for the
  imported SIMs and days, like a CSV usage import.
- Each run with new days, and every failed run, is a row in the import history
  (`GET /api/imports?source=carrier%20feed`): `source` "carrier feed",
  `carrierName`, `totalRows`, `validRows` (imported), `invalidRows` (rejected),
  `inserted`, `updated`, `unchanged`, and `message` for a failure. A run with
  nothing new is not recorded; the carrier's `lastSyncMessage` says "Caught up".
- A run left `running` by an API restart is marked failed at the next start.

### Settings (`CarrierFeed:` section)

| Key | Default | Meaning |
| --- | ------- | ------- |
| `Enabled` | true | runs the daily background service |
| `IntervalMinutes` | 1440 | time between scheduled runs of every enabled feed |
| `StartupDelaySeconds` | 60 | wait after start before the first scheduled run |
| `RequestTimeoutSeconds` | 30 | per request |
| `MaxAttempts` | 4 | tries for 429, 5xx and timeouts |
| `RetryBaseDelayMs` | 2000 | first backoff, doubled each time |
| `MaxRetryDelaySeconds` | 30 | cap on any wait, also on `Retry-After` |
| `MaxDaysPerRun` | 31 | days pulled by one run |
| `PageSize` | 500 | summary page size |
| `DefaultLookbackDays` | 7 | first sync with no start date |
| `AllowInsecureHttp` | false | accept `http://` for non-loopback hosts |

Audit rows (category `organisation`, never the key): `carrier.feed_saved`
(`baseUrl`, `enabled`, `startDate`, `lastSyncDate`, `keySet`, `keyChanged`),
`carrier.feed_tested` (`ok`, `date`) and `carrier.feed_sync_requested`.

## Checklist for a real carrier

- [ ] The carrier offers a daily per-ICCID usage API (or file drop) and tells you
      when a day is final. Pick the sync time after that.
- [ ] Map its fields to the summary row: at least `iccid` and total MB. Ask for
      download, upload, session counts, towers, hotspot, roaming sessions and the
      remaining voice/SMS: scoring is better with them.
- [ ] Its API key arrives through a secure channel, is read-only, and can be
      rotated. Save it in Settings; never in the repo, chat or a ticket.
- [ ] The carrier signals "not published yet" and rate limits in a way that maps
      to `404 date_not_available` and `429` + `Retry-After`. If not, adapt
      `CarrierFeedClient`.
- [ ] If the real API differs from the contract above, add an adapter that
      produces `FeedRow`; keep the importer unchanged.
- [ ] Time zone: confirm which time zone the carrier uses for "a day".
- [ ] ICCID format matches yours (19 digits, no spaces or check digit differences).
- [ ] The feed address is `https://`, allowed through your firewall, and resolves
      from the API host. Consider restricting outgoing calls to known carrier hosts.
- [ ] Production: persist and share the Data Protection key ring, or move keys to
      SSM Parameter Store behind `ICarrierSecretStore`.
- [ ] Run one carrier with a start date a few days back, check the import history
      and the rejected count, then turn the daily switch on.
- [ ] Watch the first week: failed runs show in Settings and Imports with the reason.

## Smoke test

`backend/docs/carrier-feed-smoke.ps1` starts the mock itself and drives a
running Development API. See its header for the API settings and the accounts it
needs. Run it on a throwaway database, never on `simguard`.
