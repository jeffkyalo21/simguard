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
Other settings: `CarrierMock__DataDir`, `CarrierMock__SessionsFile`,
`CarrierMock__StartDate` (the first "current date"; a date, `last` for the last day in the data, or by default the first day in
the data). The admin routes have no key, so run the mock on localhost only.

In Settings, Carriers, Usage feed, use the address `http://127.0.0.1:5391`
(plain http is accepted for localhost only) and the key of that carrier.

### Demo controls

The mock serves days up to a "current date". Dates after it are `404`.

| Call | Does |
| ---- | ---- |
| `POST /admin/advance-day` | moves the current date one day on |
| `POST /admin/set-date` `{ "date": "2025-02-10" }` | sets it |
| `POST /admin/fault` `{ "fault": "rate_limit", "count": 2 }` | turns a fault on |
| `POST /admin/reset` | turns every fault off |
| `GET /admin/state` | current date and faults |

Faults are off by default: `slow` (`delayMs`, default 3000), `server_error`
(500), `empty_day` (summary has no rows), `rate_limit` (429 with
`Retry-After: 1`). `count` makes a fault switch itself off after that many
requests, so a client that retries recovers; leave it out to keep the fault on
until `reset`.

A demo, one day at a time: start the mock (current date is the first data day),
save the feed with start date `2025-01-01`, press Sync now, then call
`advance-day` and Sync now again.

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
