# Carriers and daily usage API

Contract for issue #51. Everything here needs an access token (see
[auth.md](auth.md)). JSON is camelCase, timestamps are UTC ISO 8601, dates are
`YYYY-MM-DD`. Errors use ProblemDetails like the rest of the API: 400
validation (with an `errors` object keyed by field), 401 no or invalid token,
403 wrong role, 404 not found, 409 conflict.

## Roles

| Endpoint | Admin | SecurityOfficer | Auditor | Policy |
| -------- | :---: | :-------------: | :-----: | ------ |
| `GET /api/carriers` | no | yes | yes | `OrgRead` |
| `POST /api/carriers`, `PUT/DELETE /api/carriers/{id}` | no | yes | no | `OrgWrite` |
| `PUT /api/carriers/{id}/feed`, `POST .../feed/test`, `POST .../feed/sync` | no | yes | no | `OrgWrite` |
| `GET /api/sims/{id}/usage`, `/{id}/usage/cycle` | no | yes | yes | `OrgRead` |
| `GET /api/usage/analytics`, `GET /api/tiers` | no | yes | yes | `OrgRead` |
| `PATCH /api/sims/{id}/tier` | no | yes | no | `OrgWrite` |

The full matrix is in [auth.md](auth.md).

## Carriers

A carrier is a mobile network operator. Every SIM belongs to exactly one.

```json
{ "id": 2, "name": "Safaricom", "createdAt": "2026-11-03T17:56:13Z", "simCount": 35 }
```

`name` is trimmed, 2 to 40 characters, and unique ignoring case (`airtel` and
`Airtel` are the same carrier). `simCount` is the number of SIMs that use the
carrier.

Each carrier also has usage feed fields (additive): `feedBaseUrl`,
`feedEnabled`, `feedKeySet` (whether an API key is saved; the key itself is
never returned), `feedStartDate`, `lastSyncDate`, `lastSyncStatus`
(`running`, `succeeded`, `failed`), `lastSyncAt` and `lastSyncMessage`. The
three feed endpoints are described in [carrier-feed.md](../../docs/carrier-feed.md).

### GET /api/carriers

A plain array (not paged), oldest first.

### POST /api/carriers

Body `{ "name": "Telkom" }`. 201 with the carrier (`simCount` 0) and a
`Location` header.

### PUT /api/carriers/{id}

Body `{ "name": "Safaricom PLC" }`. 200 with the carrier. Changing only the
casing of a name is allowed. Sending the current name is a no-op (200, no
audit row).

### DELETE /api/carriers/{id}

204. Blocked while SIMs use the carrier.

### Errors

| Status | When | Body |
| ------ | ---- | ---- |
| 400 | name missing, blank, shorter than 2 or longer than 40 | `errors.name` |
| 404 | unknown id (PUT, DELETE) | `detail` |
| 409 | another carrier has the name (POST, PUT) | `detail` and `errors.name`, both "A carrier named 'X' already exists." |
| 409 | SIMs use the carrier (DELETE) | `detail` ("35 SIMs use carrier 'Safaricom'. Move them to another carrier before removing it.") and `simCount` |

The duplicate name 409 has the same `errors` object as a 400, so a form can
show it next to the name field:

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "A carrier named 'Airtel' already exists.",
  "errors": { "name": ["A carrier named 'Airtel' already exists."] }
}
```

### Audit

| Action | oldValues | newValues |
| ------ | --------- | --------- |
| `carrier.created` | null | `{ id, name }` |
| `carrier.renamed` | `{ id, name }` before | `{ id, name }` after |
| `carrier.deleted` | `{ id, name }` | null |
| `carrier.feed_saved` | `{ carrierId, carrier, baseUrl, enabled, startDate, lastSyncDate, keySet, keyChanged }` before | the same after |
| `carrier.feed_tested` | null | `{ carrierId, carrier, ok, date }` |
| `carrier.feed_sync_requested` | null | `{ carrierId, carrier }` |

Feed rows never contain the API key. They are `organisation` rows.

`affectedSimId` is null on carrier rows.

### SIMs and carriers

- `POST /api/sims` and `PUT /api/sims/{id}` take `carrierId` (what the
  frontend SIM form sends) or `operator` (the carrier name, matched ignoring
  case). When both are sent, `carrierId` wins.
- Unknown carriers give 400: `errors.carrierId` ("Carrier 9 does not exist.")
  or `errors.operator` ("There is no carrier named 'X'. Add it with POST
  /api/carriers first."). Sending neither gives 400 on `carrierId`.
- A duplicate phone number or ICCID gives 409 with `detail` and, additive
  since issue #14, `errors.phoneNumber` or `errors.iccid` holding the same
  sentence, like the carrier name conflict above.
- SIM responses add `carrierId` and keep `operator`, now the carrier's name,
  so existing screens keep working. Renaming a carrier renames `operator` on
  all its SIMs.
- `GET /api/sims` takes `carrierId=` as a filter. The existing `operator=`
  filter matches the carrier name.
- The SIM audit snapshots (`sim.created`, `sim.updated`, `sim.deleted`) carry
  `carrierId` and `operator`.
- SIM responses add `allowance` (issue #14): the tier allowance of the SIM's
  tier (see [Tier allowances](#tier-allowances)), or null without a tier.
  `monthlyCost` is the stored value when it is not 0, otherwise the tier's
  `monthlyCost`, so imported SIMs no longer show 0.
- Allocation responses add `departmentName`, the department of the
  allocated employee. `assignmentDate` is the stored timestamp, unchanged.

The AddCarriers migration made one carrier per distinct old `sims.operator`
value (trimmed, compared ignoring case, cut to 40 characters, empty values
became `Unknown`) and added Safaricom, Airtel and Telkom when missing. Names
that came from old values are kept even if shorter than 2 characters.

## Daily usage

One row per SIM per day, in table `daily_usage`, unique on `(sim_id, date)`.
The columns are the ML daily usage contract
([ml-service/docs/daily_usage_schema.md](../../ml-service/docs/daily_usage_schema.md)).
Units: data in MB, durations in seconds, voice in minutes. Rows are loaded by
`ml-service/scripts/load_daily_usage.py`; there is no write endpoint.

The view `ml_daily_usage` exposes the contract columns for the ML Postgres
reader (`READER=postgres`). Give the ML service a user that can only SELECT
from it:

```sql
GRANT SELECT ON ml_daily_usage TO simguard_ml;
```

A SIM with usage rows cannot be deleted (409 "SIM 125 has usage history and
cannot be deleted.").

### GET /api/sims/{id}/usage?from=&to=

`from` and `to` are optional dates and both are included in the range.
Without `to`, the range ends on the SIM's latest day with usage (today when it
has none). Without `from`, it covers the 30 days up to `to`.

```json
{
  "simId": 125,
  "from": "2025-03-02",
  "to": "2025-03-31",
  "totals": { "days": 30, "totalMb": 10412.551, "billedMb": 10412.5, "voiceMinutesUsed": 98.4, "smsCount": 61 },
  "asOf": "2025-03-31",
  "days": [
    {
      "date": "2025-03-02",
      "totalDownloadMb": 263.7242, "totalUploadMb": 227.4051,
      "dataSessionCount": 2, "dataDurationS": 478,
      "hotspotSessions": 0, "publicApnSessions": 0,
      "uniqueTowers": 1, "geoFlagCount": 0,
      "peakMb": 184.76, "offpeakMb": 306.37, "totalMb": 491.13,
      "roamingSessions": 0, "billedMb": 491.13, "tier": "professional",
      "voiceMinutesUsed": 3.867, "smsCount": 4,
      "voiceMinutesRemaining": 196.13, "smsRemaining": 131
    }
  ]
}
```

`days` is oldest first and has only the days that have a row: missing days
are not filled with zeros. Totals are sums over `days`, rounded to 3 decimals.
`asOf` (added for issue #14) is the latest `daily_usage` date of the whole
fleet, or null without usage.

| Status | When |
| ------ | ---- |
| 400 | `from` after `to` (`errors.from`), range longer than 366 days (`errors.to`), dates not `YYYY-MM-DD` |
| 404 | unknown SIM |

### GET /api/sims/{id}/usage/cycle

The 30-day billing cycle (numbered from 0, cycle 0 starts on 2025-01-01, as
in the tier recommendations) that holds the fleet's as-of date, up to that date.

```json
{
  "simId": 125,
  "asOf": "2025-03-31",
  "cycle": 2,
  "cycleStart": "2025-03-02",
  "cycleEnd": "2025-03-31",
  "cycleLength": 30,
  "daysElapsed": 30,
  "tier": "professional",
  "allowance": { "tier": "professional", "dataMb": 40960, "voiceMinutes": 200, "sms": 135, "monthlyCost": 2500.00 },
  "used": { "dataMb": 10412.6, "voiceMinutes": 98.4, "sms": 61 },
  "days": [{ "date": "2025-03-02", "dataMb": 491.1, "cumulativeDataMb": 491.1, "voiceMinutes": 3.9, "sms": 4 }]
}
```

`tier` is the SIM's tier, or the tier of its latest usage row in the cycle
when the SIM has none; `allowance` is null when neither is known. Without any
usage in the fleet, `asOf`, `cycle`, `cycleStart` and `cycleEnd` are null and
`days` is empty. 404 for an unknown SIM.

### GET /api/usage/analytics?days=&departmentId=&tier=

Fleet usage for the `days` days (`7`, `30` or `90`, default 30) up to the
as-of date. `departmentId` keeps SIMs whose current allocation is in that
department; `tier` keeps SIMs of that tier (one of the four, ignoring case).

```json
{
  "days": 30,
  "asOf": "2025-03-31",
  "from": "2025-03-02",
  "to": "2025-03-31",
  "totals": { "simCount": 1000, "dataUsedMb": 1004311.4, "dataPaidMb": 1310720.0, "unusedMb": 312004.2, "utilisationPercent": 77 },
  "daily": [{ "date": "2025-03-02", "dataMb": 33410.2 }],
  "byDepartment": [{ "name": null, "simCount": 1000, "dataUsedMb": 1004311.4, "dataPaidMb": 1310720.0 }],
  "byTier": [{ "name": "field", "simCount": 250, "dataUsedMb": 401000.1, "dataPaidMb": 512000.0 }],
  "topSims": [
    { "simId": 125, "phoneNumber": "+254700000125", "employeeName": null, "departmentName": null,
      "tier": "field", "carrier": "Safaricom", "dataUsedMb": 3100.2, "dataPaidMb": 3413.3, "utilisationPercent": 91 }
  ],
  "leastUsedSims": []
}
```

Only SIMs with at least one usage row in the period count. Paid data is the
tier bundle times `days`/30. `topSims` are the 10 SIMs with most data used;
`leastUsedSims` the 10 with the lowest share of paid data used. Groups are
sorted by data used; a null `name` means unallocated (department) or no tier.
MB values are rounded to 1 decimal, percents to whole numbers. Without usage
every list is empty and `asOf` is null. 400 for another `days` value or an
unknown tier.

## Tier allowances

### GET /api/tiers

The table `tier_allowances`, smallest bundle first:

| tier | dataMb | voiceMinutes | sms | monthlyCost (KES) |
| ---- | -----: | -----------: | --: | ----------------: |
| support | 20480 | 100 | 70 | 1500 |
| professional | 40960 | 200 | 135 | 2500 |
| manager | 61440 | 300 | 205 | 3500 |
| field | 102400 | 500 | 340 | 5000 |

Data bundles match `TIER_BUNDLE_GB` in the ML service (1 GB = 1024 MB). Voice
and SMS are the cycle totals in the demo usage (used plus remaining). The
costs are demo values: the source data has no prices. The rows are seeded by
the AddTierAllowances migration; there is no write endpoint. `sims.tier` is a
foreign key to this table.

### Differences from the frontend mock

The frontend `getSimUsage` (src/lib/data.js) returns a billing cycle view:
`{ cycleLength, cycleDay, series: [{ day, gb }], data: { used, limit }, voice, sms }`
with `gb` cumulative. This endpoint returns raw days instead:

- MB per day, not cumulative GB. Cumulative GB is a running sum of `totalMb`
  divided by 1024.
- The billing cycle view and plan limits are in `GET /api/sims/{id}/usage/cycle`.
- `getUsageAnalytics` maps to `GET /api/usage/analytics`.

The carrier items have no `status` ("connected" / "not connected") field;
the backend has nothing to base it on, so the frontend shows no status badge.

## SIM tier

`sims.tier` is the SIM's ML plan tier: `field`, `manager`, `professional` or
`support` (the tiers in the ML contract and the cohort keys in
`ml-service/artifacts/cohort_stats.json`). The tier decides which ML cohort
the SIM is compared with. The view `ml_daily_usage` returns `sims.tier` in
place of the tier stored on each usage row when it is set, so a change
affects every score computed after it (and any rescoring of past days); it
does not change scores already stored. `GET /api/sims/{id}/usage` still shows
the tier stored on each usage row.

The AddSimTier migration set the tier of every SIM with usage from its most
recent usage row. SIMs without usage have no tier (`null`) until one is set.
Every SIM response (`GET /api/sims`, `GET /api/sims/{id}`, POST, PUT and
the status PATCH) includes `tier` (one of the four tiers, or `null`), placed
after `status`.

`sims.iccid` already exists (required, unique, at most 30 characters), so no
ICCID migration was needed. The SIM endpoints keep their current ICCID
check; the CSV import (see [imports.md](imports.md)) is stricter and accepts
18 to 22 digits only.

### PATCH /api/sims/{id}/tier

SecurityOfficer only (`OrgWrite`). Request: `{ "tier": "manager" }`, trimmed
and compared ignoring case.

```json
{ "simId": 125, "tier": "manager", "previousTier": "professional" }
```

`previousTier` is `null` when the SIM had no tier. Sending the current tier
returns 200 and writes no audit entry. A change writes `sim.tier_changed`
with `oldValues {tier}` and `newValues {tier}`.

| Status | When |
| ------ | ---- |
| 400 | tier missing (`errors.tier` "tier is required.") or not one of the four tiers |
| 404 | unknown SIM |

The frontend mock `setSimTier` also changes plan limits and cost; this
endpoint only changes the tier.

## Smoke test

[carriers-smoke.ps1](carriers-smoke.ps1) checks every endpoint here against a
running Development API, with the same credentials setup as
[alerts-smoke.ps1](alerts-smoke.ps1). The usage checks expect 90 days of
loaded usage (2025-01-01 to 2025-03-31) for `-UsageSimId` (default 125), so
run `ml-service/scripts/load_daily_usage.py` first. The last check expects a
409 when deleting that SIM; pick a SIM with no allocations, alerts or risk
scores if you want that 409 to come from the usage rule.

The script creates a carrier and two SIMs and removes them again; it leaves
the two test accounts and the audit rows.
