# Scoring pipeline

Issue #14. The backend asks the ML service to score SIM-days, stores the
results as risk scores, raises alerts from sustained patterns and stores tier
recommendations. Pages never call the ML service: they read the stored
results, so everything keeps working while the ML service is down.

All dates are UTC calendar dates. All times are UTC.

## ML service connection

| Setting | Default | Notes |
| ------- | ------- | ----- |
| `MlService:BaseUrl` | `http://127.0.0.1:8001` | appsettings or `MlService__BaseUrl` |
| `MlService:SharedSecret` | none | user-secrets or `MlService__SharedSecret`; never committed |
| `MlService:TimeoutSeconds` | 120 | `/v1/score`, `/version` |
| `MlService:RecommendTimeoutSeconds` | 600 | `/v1/recommend-tier` |
| `MlService:HealthTimeoutSeconds` | 5 | `/health` |
| `MlService:RetryDelaySeconds` | 1 | one retry on a transient failure (connection error, timeout, 5xx) |

The backend sends the secret in the `X-SimGuard-ML-Secret` header. The ML
service reads it from `ML_SHARED_SECRET` and requires it on every endpoint
except `/health` (401 otherwise). Without `ML_SHARED_SECRET` the ML service
starts only when `ML_ENVIRONMENT=Development`, with the check off and a
warning; in any other environment it refuses to start.

Set both sides to the same value, for example:

```powershell
dotnet user-secrets set "MlService:SharedSecret" "<secret>" --project SimGuard.Api
$env:ML_SHARED_SECRET = '<secret>'
```

The ML service reads the daily data itself from Postgres (`READER=postgres`,
`ML_DATABASE_URL`, view `ml_daily_usage`). The backend sends only SIM ids and
an as-of date; it never sends features.

## Scoring run

A run scores dates in ascending order.

1. Candidates: a SIM can be scored for date D only when it has usage on all
   15 days ending on D (`SCORE_LOOKBACK_DAYS` = 15 in the ML service). A
   (SIM, date) that already has a risk score is skipped unless the run is
   forced.
2. For each date with at least one candidate, the SIM ids go to
   `POST /v1/score` with `asOfDate` = D in batches of `Scoring:BatchSize`
   (default 1000; the contract maximum is 5000).
3. Each result is checked (known SIM, probability 0 to 1, known gate
   decision) and upserted into `risk_scores` on (SIM, date): score, bucket
   (gate decision), top reasons and the model version from `/version`.
   Repeating a run creates no duplicates.
4. Alert rules run for that date (below), then the next date.

Gate bands come from the ML service (`ml-service/README.md`): `clear` below
0.10, `flag` above 0.90, `escalated` otherwise. The backend stores the
decision the ML service returns.

Only one run executes at a time. A request that arrives while a run is
queued is merged into the queued run (its SIMs and dates are added and
`merged_requests` goes up); a request during a running run queues the next
run. Tier recommendation runs share the same lock, so the two never overlap.
A run left `running` by an API restart is finished at startup as `partial`
(some SIM-days scored) or `failed`; its scores are kept and the next run
continues. Queued runs stay queued and run after the restart.

Run one API instance per database. The run lock is held in the process, and
an instance that starts finishes every `running` run as interrupted, so a
second instance on the same database can mark a live run of the first one as
`partial` and take its queued runs. The first instance still finishes the
live run and overwrites the status; a run taken by the second instance is
scored by the next run.

### Run record

`scoring_runs`: `trigger` (`schedule`, `manual`, `import`), `status`
(`queued`, `running`, `succeeded`, `partial`, `failed`), `force`, scope (SIM
ids and date range, null for a full run), `requested_by` (user email or
`system`), `requested_at`, `started_at`, `finished_at`, `first_date`,
`last_date`, `dates_scored`, `sim_days_scored`, `sim_days_skipped`,
`failures`, `alerts_created`, `alerts_updated`, `error_summary`,
`model_version`.

### Failure behaviour

- ML service unreachable or `/version` failing: the run ends `failed` before
  any write. Existing scores and alerts are kept.
- A batch fails (after the one retry) or returns an invalid result: nothing
  from that batch is written, the run stops at that date and ends `partial`
  (some SIM-days scored) or `failed` (none). Dates already finished stay, so the
  next run continues where this one stopped.
- Every failed or partial run is audited as `scoring.run_failed` with actor
  `system`, category `system`, and the error summary. No SIM ids are logged.

## Alert rules

The ML documentation (`ml-service/README.md`, `ml-service/docs/`) does not
define a sustained pattern or a cooldown, so these defaults from issue #14
apply. The flagged-day condition was added after the demo backfill (below).
They are in configuration:

| Rule | Setting | Default |
| ---- | ------- | ------- |
| Sustained: at least N non-clear days ... | `Scoring:SustainedMinDays` | 3 |
| ... among the last M scored days up to D | `Scoring:SustainedWindowDays` | 5 |
| ... of which at least K are `flag` | `Alerts:MinFlaggedDaysInWindow` | 1 |
| No new alert after a false positive for | `Scoring:FalsePositiveCooldownDays` | 7 days |

For each scored date D and each SIM whose score on D is `flag` or
`escalated`:

- Sustained means at least 3 of the SIM's last 5 scored days up to and
  including D are `flag` or `escalated`.
- And at least 1 of those 5 days is `flag` (score above the flag threshold).
  A SIM that is only `escalated` keeps its stored scores but raises no alert
  and does not update an open one. With `MinFlaggedDaysInWindow` set to 0
  the escalated-only rule applies again.
- One open alert per SIM. If the SIM has an open alert (`flagged` or
  `escalated`), it is updated, never duplicated: `last_detected_on` moves to
  D; an `escalated` alert becomes `flagged` (risk level `high`) when a window
  day is `flag`; when D's probability is higher than the stored score, the
  score, type, reasons and model version are replaced.
- Otherwise a new alert is created with status `flagged` if any window day is
  `flag` (always, with the default of 1), else `escalated`. `detected_on` and `last_detected_on` are D,
  `created_at` is the real time. The score is the highest probability in the
  window, the reasons are that day's top reasons.
- Cooldown: after an alert is cleared as `falsePositive`, no new alert for
  that SIM until 7 days after the last scored date the alert covered. A
  cleared alert is never reopened, and an alert cleared as `confirmedMisuse`
  is never reopened automatically; a new pattern after it raises a new alert.

Because a backfill replays history in date order, a SIM with a sustained
pattern ends with one open alert whose `detected_on` and `last_detected_on`
span the pattern, not one alert per day. The rules are the same for
scheduled, manual and import runs. Alert changes made by a run are counted on
the run, not audited one by one, so the system audit stays free of SIM-level
data.

### Alert type

`Scoring/AlertTypeMap.cs`. Only reasons with direction `up` count. Each
mapped feature names a type, and the highest type in this order wins:

| Order | Type | Features (value is the scaled value the model saw, 0 = normal) |
| ----- | ---- | -------------------------------------------------------------- |
| 1 | `inactiveBilling` | `billed_mb`, `billing_gap_mb`, `billing_ratio` with value > 0 |
| 2 | `sharing` | `hotspot_sessions`, `hotspot_session_ratio`, `hotspot_sum_7d`, `hotspot_sum_14d` |
| 3 | `reselling` | `offpeak_ratio`, `data_session_count`; or a volume feature with value > 0.5 |
| 4 | `ghostUsage` | a volume feature with value <= 0 |
| 5 | `other` | nothing above |

Volume features: `total_mb`, `total_download_mb`, `total_upload_mb`,
`peak_mb`, `offpeak_mb`, `bytes_per_session`, `vol_rolling_7d_mean`,
`vol_rolling_7d_std`, `vol_rolling_14d_mean`, `vol_rolling_14d_std`,
`vol_zscore_7d`, `vol_zscore_14d`, `trend_slope_14d`, `cohort_z_total_mb`,
`cohort_z_vol_zscore_7d`, `cohort_z_vol_zscore_14d`. Other features (geo,
roaming, tier, `cohort_z_iso_anomaly_score`) do not decide.

Order, not contribution, decides because a low `peak_mb` ranks first for
reselling, ghost usage and inactive billing alike. On the demo data the
labelled SIMs with an alert were typed: sharing 55 of 61, reselling 32 of 37,
ghost usage 35 of 40. Inactive billing SIMs come out as `ghostUsage`: their
top reasons hold no billing feature above normal, so the two cannot be told
apart from the reasons.

Reasons are stored in the alert contract format with real feature names:
`[{feature, value, contribution, direction}]`.

## Triggers and schedule

| Trigger | When | Actor in audit |
| ------- | ---- | -------------- |
| `schedule` | daily at `Scoring:DailyRunTimeNairobi` (default `02:00`, Africa/Nairobi, UTC+3) while `Scoring:Enabled` (default `true`) | `system`, category `system` |
| `manual` | `POST /api/admin/scoring/run` | the Admin's email |
| `import` | after every usage import commit that inserted or updated rows | `system`, category `system` |

The schedule lives in configuration only (appsettings, or
`Scoring__DailyRunTimeNairobi` and `Scoring__Enabled`); there are no settings
endpoints for it. With `Scoring:Enabled` false, neither the schedule nor an
import starts a run; Run now still works.

An import run covers the imported SIMs from the first imported date to the
last imported date plus 14 days, the dates whose 15-day window the new rows
can change. Already scored SIM-days are skipped, so it scores only what
became scorable.

## Endpoints

### POST /api/admin/scoring/run

`AdminOnly`. Queues a manual run of every scorable SIM-day not yet scored and
returns at once. 202: `{ "runId": 4, "merged": false }`; `merged` is true when
the request joined an already queued run. Audited as `scoring.run_requested`.

### GET /api/admin/scoring/status

`AdminOnly`. Totals only, never SIM-level data.

```json
{
  "enabled": true,
  "dailyRunTimeNairobi": "02:00",
  "nextScheduledRunAt": "2026-11-04T23:00:00Z",
  "mlService": { "configured": true, "reachable": true, "status": "ok", "detail": null, "modelVersion": "v1-7b2df575ba8a" },
  "totals": {
    "riskScores": 76000,
    "latestUsageDate": "2025-03-31",
    "latestScoredDate": "2025-03-31",
    "latestScoredDateBuckets": { "clear": 782, "escalated": 194, "flag": 24 }
  },
  "runs": [
    {
      "id": 2, "trigger": "manual", "status": "succeeded", "force": false,
      "scopeSimCount": null, "scopeFrom": null, "scopeTo": null, "mergedRequests": 0,
      "requestedBy": "admin@simguard.local", "requestedAt": "2026-11-04T11:30:00Z",
      "startedAt": "2026-11-04T11:30:01Z", "finishedAt": "2026-11-04T11:30:03Z",
      "firstDate": null, "lastDate": null, "datesScored": 0, "simDaysScored": 0,
      "simDaysSkipped": 0, "failures": 0, "alertsCreated": 0, "alertsUpdated": 0,
      "errorSummary": null, "modelVersion": "v1-7b2df575ba8a"
    }
  ]
}
```

`runs` holds the last 10 runs, newest first. `mlService.status` is the
status the ML `/health` returns (`ok`) when reachable, otherwise
`unreachable` (no connection or timeout) or `unavailable` (error response),
with the reason in `detail`. `modelVersion` is null when `/version` fails,
for example with a wrong shared secret.

### GET /health

Anonymous. Checks the database only, for hosting: 200
`{ "status": "healthy" }` or 503 `{ "status": "unhealthy" }`. It does not
call the ML service.

## Tier recommendations

Endpoints and response shapes: [api-tier-recommendations.md](api-tier-recommendations.md).

- A run sends the SIMs in batches of `TierRecommendations:BatchSize` (default
  200, the contract maximum) to `POST /v1/recommend-tier` for the 30-day
  billing cycle (counted from 2025-01-01) that holds the latest usage date.
- Evaluated: SIMs with a tier and usage in that cycle, without an open
  recommendation. A result is stored only for an upgrade or downgrade, and
  not when the same recommended tier was dismissed for that SIM in the last
  90 days. Nothing is applied; `PATCH /api/sims/{id}/tier` applies a tier.
- Interval: `IntervalDays` (90 to 365, default 90, changed with
  `PUT /api/tier-recommendations/settings` and audited with the old and new
  value). A manual run is refused with 409 and the next allowed date when the
  last run that did not fail started less than `IntervalDays` ago. A first
  run is always allowed, and a failed run does not start the interval.
  `?force=true` skips the check in Development only.
- Automatic run: checked every `TierRecommendations:CheckIntervalHours`
  (default 24); starts when the last run that did not fail started more than
  `IntervalDays` + `TierRecommendations:AutoRunGraceDays` (default 14) days
  ago. None is due before the first manual run. Audited as `system`.
- A batch that fails ends the run `partial` (results so far are kept) or
  `failed`.
- Timing for 1000 SIMs, ML service on the same machine: 51 seconds for
  billing cycle 2 (the demo data, precomputed Prophet forecasts), 659 seconds
  (about 0.66 seconds per SIM) for billing cycle 6, where the ML service fits
  Prophet live. A run is a background job at most every 90 days, so this is
  usable; each batch of 200 stays well under the 600 second timeout.

## Roles

| Action | Admin | SecurityOfficer | Auditor |
| ------ | :---: | :-------------: | :-----: |
| Run now, scoring status | yes | no | no |
| Read alerts, risk scores, tier recommendations, tier settings and status | no | yes | yes |
| Change alerts, tier settings, start a tier run, dismiss | no | yes | no |

## Verified on demo data

Fresh database, demo files from `ml-service/data/import_samples` loaded with
`backend/scripts/import-demo-data.ps1` (1000 SIMs, 90000 usage rows), ML
service with `READER=postgres`:

- The usage import triggered the backfill: 76 dates (2025-01-15 to
  2025-03-31), 76000 SIM-days, 0 failures, about 190 seconds.
- Last date: 782 clear, 194 escalated, 24 flag (evaluation report test
  buckets: 70 percent clear, 29 percent escalated, 2 percent flag).
- A second run scored nothing new: 76000 risk scores, no duplicate (SIM,
  date), no SIM with more than one open alert.
- With the ML service stopped, Run now ended `failed` with the reason, and no
  score or alert changed.
- Alerts against the generator labels (`ml-service/data/synthetic_cdr_labels.csv`,
  162 anomalous SIMs):

  | Rule | Alerts | On anomalous SIMs | On other SIMs | Anomalous SIMs without alert |
  | ---- | -----: | ----------------: | ------------: | ---------------------------: |
  | 3 of 5 non-clear | 894 | 160 | 734 | 2 |
  | 3 of 5 non-clear and 1 flag (current) | 154 | 143 | 11 | 19 |

  Escalated days are frequent (about 55 percent of SIMs on some February
  days), so the sustained rule alone is met by most normal SIMs. The SIMs
  missed by the current rule: 9 sharing, 6 reselling, 3 ghost usage, 1
  inactive billing.
