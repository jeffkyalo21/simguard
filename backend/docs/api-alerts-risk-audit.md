# Alerts, risk score and audit log API

Contract for issue #13. Everything here needs an access token (see
[auth.md](auth.md)). JSON is camelCase, timestamps are UTC ISO 8601
(`2026-11-03T09:15:00Z`), dates are `YYYY-MM-DD`. Errors use ProblemDetails
like the rest of the API: 400 validation (with an `errors` object keyed by
field), 401 no or invalid token, 403 wrong role, 404 not found, 409 conflict.

Paged lists use the shared shape and the shared paging rules (`page` from 1,
`pageSize` default 20, max 100, larger values are clamped):

```json
{ "items": [], "page": 1, "pageSize": 20, "total": 0 }
```

## Vocabulary

| Field | Values |
| ----- | ------ |
| alert `type` | `inactiveBilling`, `sharing`, `reselling`, `ghostUsage`, `other` (the ML flags a SIM-day but does not predict the category; issue #14 assigns the type from the top reasons, `other` when it cannot) |
| alert `status` | `flagged`, `escalated`, `cleared` |
| alert `resolution` | `confirmedMisuse`, `falsePositive` (only on cleared alerts) |
| risk `bucket` | `flag`, `escalated`, `clear` |
| `riskLevel` | `high`, `medium`, `low` |
| reason `direction` | `up` (pushes the risk up), `down` (pushes it down) |

`riskLevel` is never computed from a probability threshold in the backend. It
follows the bucket the model assigned: `flag` = `high`, `escalated` =
`medium`, `clear` = `low`. An alert keeps the level of the bucket that raised
it (`flagged` alerts start `high`, `escalated` alerts start `medium`); a status
change does not change it.

`riskScore` (alerts) is a number from 0 to 1 with two decimals.
`probability` (risk scores) is a number from 0 to 1 with up to four decimals.

A reason explains one feature's share of a score:

```json
{ "feature": "distinctDevices7d", "value": 4, "contribution": 0.31, "direction": "up" }
```

## Roles

| Endpoint | Admin | SecurityOfficer | Auditor | Policy |
| -------- | :---: | :-------------: | :-----: | ------ |
| `GET /api/alerts`, `GET /api/alerts/{id}` | yes | yes | yes | `CanRead` |
| `PATCH /api/alerts/{id}/status` | yes | yes | no | `CanReviewAlerts` |
| `GET /api/sims/{id}/risk` | yes | yes | yes | `CanRead` |
| `GET /api/risk-scores` | yes | yes | yes | `CanRead` |
| `GET /api/sims/{id}/activity` | yes | yes | yes | `CanRead` |
| `GET /api/dashboard/summary` | yes | yes | yes | `CanRead` |
| `GET /api/audit-logs` | yes | no | yes | `CanReadAudit` |
| `POST /api/dev/seed-demo` (Development only) | yes | no | no | `CanUseDevTools` |

## Alerts

### Alert list item

Returned by `GET /api/alerts` and in `needsReview` on the dashboard.
`employeeName` and `departmentName` are the SIM's current allocation, or
null when the SIM is not allocated.

```json
{
  "id": 17,
  "simId": 4,
  "simPhoneNumber": "+254712345678",
  "type": "sharing",
  "riskScore": 0.94,
  "riskLevel": "high",
  "status": "flagged",
  "createdAt": "2026-11-02T14:05:00Z",
  "resolvedAt": null,
  "reasons": [
    { "feature": "distinctDevices7d", "value": 4, "contribution": 0.31, "direction": "up" },
    { "feature": "distinctCellsPerDay", "value": 19, "contribution": 0.22, "direction": "up" }
  ],
  "modelVersion": "2026.10.1",
  "employeeName": "Jane Wanjiru",
  "departmentName": "Field Sales"
}
```

### GET /api/alerts

Query:

| Parameter | Type | Notes |
| --------- | ---- | ----- |
| `status` | string | one of the statuses |
| `type` | string | one of the types |
| `simId` | int | |
| `minScore` | number | 0 to 1, inclusive |
| `from`, `to` | timestamp | filter on `createdAt`, inclusive, e.g. `2026-11-01T00:00:00Z` |
| `sort` | string | `createdAt` (default) or `riskScore` |
| `order` | string | `desc` (default) or `asc` |
| `page`, `pageSize` | int | |

200: `PagedResult<AlertListItem>`. Ties are broken by `id` descending.
400: unknown `status`, `type`, `sort` or `order`; `minScore` outside 0 to 1;
`from` after `to`; bad paging.

### GET /api/alerts/{id}

200:

```json
{
  "id": 17,
  "simId": 4,
  "simPhoneNumber": "+254712345678",
  "type": "sharing",
  "riskScore": 0.94,
  "riskLevel": "high",
  "status": "cleared",
  "createdAt": "2026-11-02T14:05:00Z",
  "resolvedAt": "2026-11-03T08:30:00Z",
  "reasons": [
    { "feature": "distinctDevices7d", "value": 4, "contribution": 0.31, "direction": "up" }
  ],
  "modelVersion": "2026.10.1",
  "resolution": "falsePositive",
  "resolutionNote": "Device swap approved by IT.",
  "resolvedBy": { "id": 2, "name": "Smoke Officer", "email": "officer@simguard.local" },
  "sim": {
    "id": 4,
    "phoneNumber": "+254712345678",
    "iccid": "8925499000000000004",
    "operator": "Safaricom",
    "planType": "Postpaid",
    "status": "assigned"
  },
  "currentAllocation": {
    "allocationId": 9,
    "employeeId": 12,
    "employeeName": "Jane Wanjiru",
    "departmentName": "Field Sales",
    "assignedAt": "2026-09-01T07:00:00Z"
  },
  "recentAlerts": [
    {
      "id": 11,
      "type": "sharing",
      "riskScore": 0.58,
      "riskLevel": "medium",
      "status": "escalated",
      "createdAt": "2026-10-05T10:00:00Z",
      "resolvedAt": null
    }
  ]
}
```

`currentAllocation` is null when the SIM is not allocated. `recentAlerts`
holds the SIM's 5 most recent other alerts, newest first. `resolvedBy` is null
unless the alert is cleared. 404: unknown alert.

### PATCH /api/alerts/{id}/status

Admin or SecurityOfficer.

```json
{ "status": "cleared", "resolution": "falsePositive", "resolutionNote": "Device swap approved by IT." }
```

Allowed transitions; anything else is 409:

| From | To | Meaning | Body |
| ---- | -- | ------- | ---- |
| `flagged` | `cleared` | close | `resolution` required, `resolutionNote` optional |
| `escalated` | `cleared` | close | `resolution` required, `resolutionNote` optional |
| `cleared` | `escalated` | reopen | no `resolution`, no `resolutionNote` |
| `escalated` | `flagged` | analyst confirms | no `resolution`, no `resolutionNote` |

- Clearing sets `resolvedAt` (now), `resolvedBy` (the caller), `resolution`
  and `resolutionNote`. Reopening clears all four.
- Every successful change writes an audit log row
  (`action: "alert.status_changed"`, see below).
- 200: the alert detail above, after the change.
- 400: missing or unknown `status`; `resolution` missing when clearing, or
  unknown; `resolution` or `resolutionNote` sent on a transition that is not a
  close; `resolutionNote` longer than 500 characters.
- 404: unknown alert. 409: transition not allowed (including "to the same
  status"), body names the current status.

## Risk scores

Issue #14 fills `risk_scores` from the ML service: one row per SIM per day
(unique on `simId` + `date`).

### Risk score item

```json
{
  "id": 501,
  "simId": 4,
  "simPhoneNumber": "+254712345678",
  "date": "2026-11-02",
  "probability": 0.9132,
  "bucket": "flag",
  "riskLevel": "high",
  "reasons": [
    { "feature": "distinctDevices7d", "value": 4, "contribution": 0.31, "direction": "up" }
  ],
  "modelVersion": "2026.10.1",
  "createdAt": "2026-11-03T02:00:00Z"
}
```

### GET /api/sims/{id}/risk

Query: `days` (default 90, 1 to 365). History covers the last `days`
calendar days up to today (UTC), oldest first.

200:

```json
{
  "simId": 4,
  "days": 90,
  "latest": {
    "date": "2026-11-02",
    "probability": 0.9132,
    "bucket": "flag",
    "riskLevel": "high",
    "reasons": [
      { "feature": "distinctDevices7d", "value": 4, "contribution": 0.31, "direction": "up" }
    ],
    "modelVersion": "2026.10.1",
    "createdAt": "2026-11-03T02:00:00Z"
  },
  "history": [
    { "date": "2026-08-05", "probability": 0.1204, "bucket": "clear" },
    { "date": "2026-11-02", "probability": 0.9132, "bucket": "flag" }
  ]
}
```

A SIM without scores returns 200 with `latest: null` and `history: []`.
`latest` is the most recent score of all time, even if it is older than the
history window. 400: `days` outside 1 to 365. 404: unknown SIM only.

### GET /api/risk-scores

Query: `simId`, `from`, `to` (dates, inclusive, e.g. `2026-10-01`), `bucket`,
`page`, `pageSize`. Newest date first, then `simId`.

200: `PagedResult<RiskScoreItem>`. 400: unknown `bucket`, `from` after `to`,
bad paging.

## SIM activity

### GET /api/sims/{id}/activity

The SIM profile timeline, newest first. Query: `page`, `pageSize`.

```json
{
  "items": [
    {
      "kind": "alertCleared",
      "occurredAt": "2026-11-03T08:30:00Z",
      "summary": "sharing alert cleared (falsePositive)",
      "actorEmail": "officer@simguard.local",
      "action": null,
      "referenceType": "alert",
      "referenceId": 17
    },
    {
      "kind": "audit",
      "occurredAt": "2026-10-20T09:00:00Z",
      "summary": "sim.status_changed",
      "actorEmail": "admin@simguard.local",
      "action": "sim.status_changed",
      "referenceType": "auditLog",
      "referenceId": 88
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 2
}
```

| `kind` | Source | `referenceType` |
| ------ | ------ | --------------- |
| `alertOpened` | alert `createdAt` | `alert` |
| `alertCleared` | alert `resolvedAt` (cleared alerts) | `alert` |
| `allocationStarted` | allocation `assignmentDate` | `allocation` |
| `allocationEnded` | allocation `returnDate` | `allocation` |
| `audit` | audit log rows for the SIM | `auditLog` |

Audit rows whose action starts with `alert.` or `allocation.` are left out
because the alert and allocation entries already show them. `actorEmail` is
the user who cleared the alert, or the acting user of the audit row; for
allocations it is null (the assigner is an employee, named in `summary`).
404: unknown SIM.

## Dashboard

### GET /api/dashboard/summary

Query: `days` = `7`, `30` or `90` (default 7). Any other value is 400.

200:

```json
{
  "days": 7,
  "from": "2026-10-27T09:00:00Z",
  "to": "2026-11-03T09:00:00Z",
  "activeSims": 120,
  "flagged": 12,
  "toReview": 8,
  "resolved": 5,
  "alertsByType": {
    "inactiveBilling": 3,
    "sharing": 7,
    "reselling": 4,
    "ghostUsage": 6,
    "other": 0
  },
  "needsReview": [],
  "tierChanges": [],
  "dataWaste": null
}
```

| Field | Meaning |
| ----- | ------- |
| `activeSims` | SIMs whose status is not `inactive` (now) |
| `flagged` | alerts with status `flagged` (now, any age) |
| `toReview` | alerts with status `escalated` (now, any age) |
| `resolved` | alerts cleared within the window (`resolvedAt` in `from`..`to`) |
| `alertsByType` | alerts created within the window, by type |
| `needsReview` | the 5 most recent `flagged` or `escalated` alerts, as alert list items (with SIM and reasons) |
| `tierChanges` | always `[]` for now: needs usage ingestion (later issue) |
| `dataWaste` | always `null` for now: needs usage ingestion and #14 |

## Audit log

### Recorded actions

Every state-changing endpoint writes one row in the same database
transaction as the change. `actorUserId` and `actorEmail` come from the
caller's access token (`sub`, `email`); `actorEmail` is a snapshot, so it
stays readable if the account is later changed. `oldValues` and `newValues`
are the relevant fields before and after (null for create or delete). No
passwords, password hashes, tokens or MFA secrets are ever stored.

| Action | Endpoint | `affectedSimId` |
| ------ | -------- | --------------- |
| `department.created`, `department.updated`, `department.deleted` | `/api/departments` | null |
| `employee.created`, `employee.updated`, `employee.deleted` | `/api/employees` | null |
| `sim.created`, `sim.updated`, `sim.status_changed` | `/api/sims` | the SIM |
| `sim.deleted` | `DELETE /api/sims/{id}` | null (the SIM is gone; its id is in `oldValues`) |
| `allocation.created`, `allocation.returned` | `/api/allocations` | the SIM |
| `alert.status_changed` | `PATCH /api/alerts/{id}/status` | the alert's SIM |
| `user.created`, `user.role_changed`, `user.disabled`, `user.enabled`, `user.mfa_reset`, `user.password_reset` | `/api/users` | null |
| `dev.demo_seeded` | `POST /api/dev/seed-demo` | null |

Sign-in, MFA enrolment and a user's own password change are recorded in the
auth event log (`GET /api/auth/events`), not here.

### GET /api/audit-logs

Admin or Auditor (SecurityOfficer gets 403). Query: `simId`, `actorEmail`
(case-insensitive, exact), `action` (exact), `from`, `to` (timestamps,
inclusive), `page`, `pageSize`. Newest first.

200:

```json
{
  "items": [
    {
      "id": 88,
      "timestamp": "2026-11-03T08:30:00Z",
      "actorUserId": 2,
      "actorEmail": "officer@simguard.local",
      "employeeId": null,
      "action": "alert.status_changed",
      "affectedSimId": 4,
      "affectedSimPhoneNumber": "+254712345678",
      "oldValues": { "alertId": 17, "status": "flagged", "resolution": null, "resolutionNote": null, "resolvedAt": null, "resolvedByUserId": null },
      "newValues": { "alertId": 17, "status": "cleared", "resolution": "falsePositive", "resolutionNote": "Device swap approved by IT.", "resolvedAt": "2026-11-03T08:30:00Z", "resolvedByUserId": 2 }
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 1
}
```

`employeeId` is the legacy `audit_logs.user_id` column (an Employee id). It is
kept for older rows and is null on rows written by the API.
`affectedSimPhoneNumber` is null when the row has no SIM. 400: `from` after
`to`, bad paging.

## Demo data (Development only)

### POST /api/dev/seed-demo

Admin only, and mapped only when `ASPNETCORE_ENVIRONMENT` is `Development`; in
any other environment the route does not exist (404). No body.

- Deletes the alerts and risk scores it created before (those with
  `modelVersion: "demo"`) and nothing else.
- Creates about 60 alerts on existing SIMs: all four types, a mix of
  `flagged`, `escalated` and `cleared` (cleared ones with `resolvedAt`,
  a `resolution` and the caller as `resolvedBy`), with reasons that fit the
  type, spread over the last 90 days.
- Creates 90 days of daily risk scores for up to 30 SIMs.
- Writes one `dev.demo_seeded` audit row.

200:

```json
{ "simsUsed": 30, "alertsCreated": 60, "riskScoresCreated": 2700, "alertsRemoved": 0, "riskScoresRemoved": 0 }
```

409: there are no SIMs yet (create some first).

## Database

Migration `AddAlertsRiskAndAudit`:

- `alerts`: `alert_type` renamed to `type`, `detected_at` renamed to
  `created_at`; new `reasons` (jsonb), `model_version`, `resolved_at`,
  `resolution`, `resolution_note` (max 500), `resolved_by_user_id` (FK to
  `app_users`). Status values `open`, `investigating`, `resolved` become
  `flagged`, `escalated`, `cleared`; risk level `critical` becomes `high`;
  types outside the vocabulary become `other`. New check constraints for
  type, status, resolution, risk level and `risk_score` between 0 and 1.
- `risk_scores`: new table (`id`, `sim_id`, `date`, `probability`, `bucket`,
  `reasons`, `model_version`, `created_at`), unique index on
  (`sim_id`, `date` descending), which also serves the latest-first lookups.
- `audit_logs`: `user_id` becomes nullable; new `actor_user_id` (FK to
  `app_users`, set to null if the user is deleted) and `actor_email`;
  `affected_sim_id` is set to null when its SIM is deleted, so audit history
  survives a SIM deletion.
