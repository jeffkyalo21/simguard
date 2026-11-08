# Tier recommendations API

Issue #14. A run asks the ML service (`POST /v1/recommend-tier`) whether each
SIM's plan tier fits its forecast usage and stores upgrades and downgrades as
open recommendations. Nothing is applied automatically: the SecurityOfficer
applies the recommended tier with `POST /api/tier-recommendations/{id}/apply`
(or changes the tier by hand with `PATCH /api/sims/{id}/tier`), or dismisses
the recommendation. Run rules, timing and failure behaviour are in
[scoring-pipeline.md](scoring-pipeline.md).

All times are UTC. Errors are RFC 7807 problem details, like the rest of the
API.

## Roles

| Endpoint | Admin | SecurityOfficer | Auditor | Policy |
| -------- | :---: | :-------------: | :-----: | ------ |
| `GET /api/tier-recommendations` | no | yes | yes | `OrgRead` |
| `GET /api/tier-recommendations/settings` | no | yes | yes | `OrgRead` |
| `GET /api/tier-recommendations/status` | no | yes | yes | `OrgRead` |
| `PUT /api/tier-recommendations/settings` | no | yes | no | `OrgWrite` |
| `POST /api/tier-recommendations/run` | no | yes | no | `OrgWrite` |
| `POST /api/tier-recommendations/{id}/apply` | no | yes | no | `OrgWrite` |
| `POST /api/tier-recommendations/{id}/dismiss` | no | yes | no | `OrgWrite` |

## Vocabulary

| Field | Values |
| ----- | ------ |
| recommendation `status` | `open`, `dismissed`, `applied` |
| `recommendation` | `upgrade`, `downgrade` |
| tier | `field`, `manager`, `professional`, `support` |
| run `trigger` | `manual`, `automatic` |
| run `status` | `running`, `succeeded`, `partial`, `failed` |

## Settings

### GET /api/tier-recommendations/settings

```json
{
  "intervalDays": 90,
  "minIntervalDays": 90,
  "maxIntervalDays": 365,
  "updatedBy": "officer@simguard.local",
  "updatedAt": "2026-11-04T09:00:00Z"
}
```

`updatedBy` and `updatedAt` are null while the default is in use.

### PUT /api/tier-recommendations/settings

Body: `{ "intervalDays": 120 }`. 200: the settings as above. 400: missing
`intervalDays`, or a value outside 90 to 365. Audited as
`tier_recommendation.settings_changed` with the old and new value.

## Runs

### POST /api/tier-recommendations/run

Starts a run in the background and returns at once.

202: `{ "runId": 3 }`.

409 when a run is already running, or when the last run that did not fail
started less than `intervalDays` ago. A first run is always allowed. The
problem names the next allowed date and also carries it as
`nextAllowedRunDate`:

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "The last tier recommendation run started less than 90 days ago. The next run is allowed from 2027-02-02 (UTC).",
  "nextAllowedRunDate": "2027-02-02"
}
```

`?force=true` skips the interval check in the Development environment only.
In any other environment the parameter is ignored. A running run still
blocks a forced one. Audited as `tier_recommendation.run_started` with the
user's email.

The background service checks every 24 hours
(`TierRecommendations:CheckIntervalHours`) and starts an automatic run when
the last run that did not fail started more than `intervalDays` plus
`TierRecommendations:AutoRunGraceDays` (default 14) days ago. An automatic
run is audited with actor `system` and category `system`. No automatic run
is due before the first manual run.

### GET /api/tier-recommendations/status

```json
{
  "lastRun": {
    "id": 3,
    "trigger": "manual",
    "status": "succeeded",
    "requestedBy": "officer@simguard.local",
    "startedAt": "2026-11-04T09:10:00Z",
    "finishedAt": "2026-11-04T09:16:30Z",
    "billingCycle": 2,
    "simsEvaluated": 1000,
    "recommendationsCreated": 212,
    "errorSummary": null
  },
  "intervalDays": 90,
  "autoRunGraceDays": 14,
  "nextAllowedRunDate": "2027-02-02",
  "automaticRunDue": false,
  "running": false
}
```

`lastRun` is the most recent run of any status, or null. `nextAllowedRunDate`
is null before the first run that did not fail. `billingCycle` is the 30-day
cycle, counted from 2025-01-01, that holds the latest usage date.

## Recommendations

### Recommendation item

```json
{
  "id": 41,
  "simId": 187,
  "simPhoneNumber": "+254712000187",
  "currentTier": "professional",
  "recommendedTier": "manager",
  "recommendation": "upgrade",
  "reason": "Forecast for the cycle starting 2025-03-02: 924 MB over the professional bundle, bundle used up in 5 days.",
  "billingCycle": 2,
  "projectedOverageMb": 924.27,
  "daysToDepletion": 5,
  "status": "open",
  "createdAt": "2026-11-04T09:12:00Z",
  "runId": 3,
  "dismissedAt": null,
  "appliedAt": null
}
```

`projectedOverageMb` is negative for a downgrade (unused bundle).

### GET /api/tier-recommendations

Query: `status` (`open`, `dismissed` or `applied`), `page`, `pageSize`. 200:
`PagedResult<TierRecommendationItem>`, newest first. 400: unknown `status`
or bad paging.

### POST /api/tier-recommendations/{id}/apply

Sets the SIM to `recommendedTier` and marks the recommendation `applied`
(with `appliedAt`), in one transaction under the SIM row lock. 200: the
applied item. 404: unknown id. 409: the recommendation is not open
(already applied or dismissed). Audited as `sim.tier_changed` (when the
tier changes) and `tier_recommendation.applied`. The SIM no longer has an
open recommendation, so a later run may evaluate it again.

### POST /api/tier-recommendations/{id}/dismiss

200: the dismissed item. 404: unknown id. 409: not open (already dismissed
or applied). Audited as
`tier_recommendation.dismissed`. A later run does not recommend the same
tier for that SIM again for 90 days.

## Database

Migration `AddTierRecommendations`:

- `tier_recommendation_settings`: single row (`id` = 1, check), `interval_days`
  (check 90 to 365, seeded 90), `updated_by_user_id`, `updated_by`,
  `updated_at`.
- `tier_recommendation_runs`: `trigger`, `status` (checks), `requested_by_user_id`,
  `requested_by`, `started_at` (indexed), `finished_at`, `billing_cycle`,
  `sims_evaluated`, `recommendations_created`, `error_summary`.
- `tier_recommendations`: `sim_id`, `run_id`, `current_tier`,
  `recommended_tier`, `recommendation`, `reason`, `billing_cycle`,
  `projected_overage_mb`, `days_to_depletion`, `status`, `created_at`,
  `dismissed_at`, `dismissed_by_user_id`; a unique partial index allows one
  open recommendation per SIM.

Migration `AddTierRecommendationApplied` (issue #14): `applied_at`,
`applied_by_user_id` (foreign key to `app_users`, set null on delete) and
`applied` in the `status` check.
