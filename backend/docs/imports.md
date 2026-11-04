# CSV imports

Bulk import of departments, employees, SIMs and daily usage from CSV files in
the standard SimGuard format. Issue #14. Everything here needs an access
token (see [auth.md](auth.md)) with the SecurityOfficer role (`OrgWrite`);
Admin and Auditor get 403.

## Endpoints

| Method and path | Success | Errors |
| --------------- | ------- | ------ |
| `GET /api/imports/{type}/template` | 200 `text/csv`, the exact header and two sample rows | 404 unknown type |
| `POST /api/imports/{type}?mode=preview` | 200 `ImportResult` (nothing is saved) | 400, 404, 413 |
| `POST /api/imports/{type}?mode=commit` | 200 `ImportResult` | 400, 404, 413, 422 |
| `POST /api/imports/{type}?mode=commit&skipInvalid=true` | 200 `ImportResult` (valid rows only) | 400, 404, 413 |
| `GET /api/imports?type=&page=&pageSize=` | 200 `PagedResult<ImportRun>`, newest first | 400 |

`{type}` is `departments`, `employees`, `sims` or `usage`. `mode` defaults to
`preview`. The upload is `multipart/form-data` with the file in a field named
`file`.

Imports are stateless: a commit reads and validates the uploaded file again
from scratch, so upload the same file for the preview and the commit. A
preview runs the whole import inside a transaction and rolls it back, so its
counts are exactly what a commit of the same file would apply at that moment.

## File format and limits

- `.csv` file name, UTF-8 (a BOM is allowed), at most 25 MB and 250000 data
  rows.
- Comma separated. Values may be in double quotes; inside quotes a quote is
  written `""`, and commas and line breaks are allowed. Lines end in CRLF or
  LF. Blank lines are skipped.
- Every cell is plain text. Leading and trailing spaces are removed and
  nothing else is interpreted: no formulas, no type guessing.
- The header must have exactly the template's columns. Case, spaces inside
  names and column order do not matter: spaces are removed and names are
  lowercased, so `Phone_Number` and ` phone_ number ` both match
  `phone_number` (but `Phone Number` becomes `phonenumber`, which does not).
  A missing, unknown or repeated
  column rejects the file with a message that lists them and the expected
  header.
- Dates are ISO `yyyy-mm-dd`, read as UTC calendar dates.
- Phone numbers are E.164 (`+254712345678`).
- Row numbers in errors count the header as row 1, as in a spreadsheet.

File-wide problems give 400 ProblemDetails (`title` "Invalid import file",
`detail` says what is wrong): not `.csv`, empty file, not valid UTF-8,
binary content, a quote that is never closed, text after a closing quote, a
row with a different number of values than the header, a value longer than
4096 characters, no header, a header with no data rows, more than 250000
rows. A file over 25 MB gives 413.

## ImportResult

```json
{
  "type": "sims",
  "mode": "preview",
  "status": "preview",
  "importRunId": null,
  "fileName": "sims.csv",
  "sha256": "9f2c...e1",
  "skipInvalid": false,
  "totalRows": 1000,
  "validRows": 998,
  "invalidRows": 2,
  "inserted": 990,
  "updated": 5,
  "unchanged": 3,
  "errorCount": 2,
  "errors": [ { "row": 14, "column": "carrier", "message": "There is no carrier named 'Orange'. Add it first." } ],
  "warningCount": 0,
  "warnings": [],
  "coverage": null
}
```

- `status` is `preview`, `committed` or `rejected`.
- `inserted`, `updated` and `unchanged` cover the valid rows. In a preview
  they are what a commit would do; after a commit, what it did. Unchanged
  rows are not rewritten.
- `errors` and `warnings` list at most the first 200 entries;
  `errorCount` and `warningCount` are the totals. `row` and `column` are null
  for a warning about the file as a whole.
- `coverage` is only set for usage imports (see below).

A commit is all or nothing. When any row has an error and `skipInvalid` is
not true, nothing is written, the run is recorded as `rejected` and the
response is 422 with the same `ImportResult` body (`status` "rejected",
counts 0). With `skipInvalid=true` the valid rows are imported and the
invalid ones are reported in `errors`.

## Import history

Every commit attempt is a row in `import_runs`, previews are not:

```json
{
  "id": 7,
  "type": "usage",
  "fileName": "usage.csv",
  "sha256": "4b1d...0a",
  "uploadedByUserId": 3,
  "uploadedByEmail": "officer@example.com",
  "startedAt": "2026-11-04T06:20:11Z",
  "finishedAt": "2026-11-04T06:20:19Z",
  "status": "committed",
  "skipInvalid": false,
  "totalRows": 90000,
  "validRows": 90000,
  "invalidRows": 0,
  "inserted": 90000,
  "updated": 0,
  "unchanged": 0,
  "errorCount": 0,
  "warningCount": 0,
  "message": null
}
```

`status` is `committed`, `rejected` (row errors without `skipInvalid`, or a
file-wide problem; `message` says why, and `sha256` is null when the file
was never read) or `failed` (an unexpected error; nothing was changed).

## Departments

Columns: `name` (required, at most 100 characters).

Upsert by name, ignoring case. A new name is inserted. A name that exists
with other casing is an update (the department is renamed to the file's
casing); the same name is unchanged. A name that appears twice in the file
(ignoring case) is a row error on the second row. Departments are never
deleted by an import.

## Employees

Columns: `name` (required, at most 150), `email` (required, valid address,
at most 150), `department` (required, the name of an existing department,
matched ignoring case), `job_role` (required, at most 100; the employee
model requires it).

Upsert by email, ignoring case. An existing employee is updated when the
name, the email's casing, the department or the job role differs, and
unchanged otherwise. An unknown department is a row error ("There is no
department named 'X'. Import or add it first."), so import departments
first. An email that appears twice in the file is a row error.

Department and employee imports write only the `import.committed` audit
entry, not one entry per row.

## SIMs

Columns:

| Column | Rule |
| ------ | ---- |
| `iccid` | required, 18 to 22 digits, the key |
| `phone_number` | required, E.164 (`+254712345678`), not used by another SIM |
| `carrier` | required, the name of an existing carrier, matched ignoring case |
| `tier` | required, `field`, `manager`, `professional` or `support` (ignoring case) |
| `status` | empty, `available` or `inactive` |

Upsert by ICCID. An import never creates carriers (an unknown carrier is a
row error) and never creates allocations, so `assigned` is not accepted.
An empty status keeps the current status; new SIMs then start as
`available`. Changing the status of a SIM with an active allocation is a
row error. An ICCID or phone number that appears twice in the file is a row
error on the later row, and so is a phone number that belongs to another
SIM (two SIMs cannot swap numbers in one import).

New SIMs get `plan_type` = the tier and `monthly_cost` = 0, because the
file has no plan or cost; edit them with `PUT /api/sims/{id}`. An existing
SIM is updated when the phone number, carrier, tier or a given status
differs; its plan type and cost are kept. The import sets `sims.tier`, which
the ML view `ml_daily_usage` uses for the SIM's cohort.

Each changed SIM gets its own audit entries, in the same shapes as the SIM
endpoints: `sim.updated` (old and new snapshot) when the phone number or
carrier changed, `sim.tier_changed` (`{ tier }` old and new) and
`sim.status_changed` (`{ status }` old and new). New SIMs are covered by the
`import.committed` entry only.

## Daily usage

Columns, in this order in the template (any order is accepted):

`iccid, date, total_download_mb, total_upload_mb, data_session_count,
data_duration_s, hotspot_sessions, public_apn_sessions, unique_towers,
geo_flag_count, peak_mb, offpeak_mb, total_mb, roaming_sessions, billed_mb,
tier, voice_minutes_used, sms_count, voice_minutes_remaining, sms_remaining`

These are `iccid` plus the ML daily usage contract
([daily_usage_schema.md](../../ml-service/docs/daily_usage_schema.md)) with
`sim_id` replaced by `iccid`. Units: data in MB, durations in seconds, voice
in minutes.

Rules (each failure is a row error):

- `iccid` must be an existing SIM (import SIMs first).
- `date` is `yyyy-mm-dd`.
- No empty values anywhere.
- `*_mb`, `voice_minutes_used` and `voice_minutes_remaining` are numbers
  with a dot for decimals; the other count columns are whole numbers.
- Numbers must not be negative, except `voice_minutes_remaining` and
  `sms_remaining`, which the ML contract allows below zero (bundle overuse;
  the generator data has such rows).
- `tier` is one of the four tiers. A tier that differs from `sims.tier` is a
  warning: the row is stored as given and scoring uses the SIM's tier.
- The same `(iccid, date)` twice in the file is an error on the later row.

Rows are upserted into `daily_usage` by `(sim_id, date)` with a binary COPY
into a temporary table and one `INSERT ... ON CONFLICT DO UPDATE ... WHERE
(old values) IS DISTINCT FROM (new values)`, so unchanged rows are counted
as `unchanged` and not rewritten. No per row audit entries are written.

### Coverage

A usage import returns `coverage` for the SIMs in the file, computed over
all their stored days after the import (for a preview: as they would be):

```json
"coverage": {
  "sims": 3,
  "firstDate": "2025-01-01",
  "lastDate": "2025-01-20",
  "minimumDays": 15,
  "scorable": 1,
  "notEnoughHistory": 2,
  "withGaps": 1,
  "details": [
    { "simId": 41, "iccid": "8925499176223501203", "firstDate": "2025-01-01", "lastDate": "2025-01-20",
      "days": 18, "gapCount": 1, "missingDays": 2, "latestRunDays": 8, "scorable": false }
  ]
}
```

- `latestRunDays` is the number of consecutive days ending on the SIM's
  last date. A SIM is `scorable` when it is at least `minimumDays` (15, the
  ML `SCORE_LOOKBACK_DAYS`), otherwise it counts as `notEnoughHistory`.
  `scorable + notEnoughHistory = sims`.
- `withGaps` counts SIMs with at least one missing day between their first
  and last date, whether or not they are scorable. `gapCount` is the number
  of gaps, `missingDays` the days missing in total.
- `details` lists at most 1000 SIMs, the ones that are not scorable first,
  then those with gaps.
- Gaps and short history are also reported as warnings (one per SIM, with
  `row` and `column` null). They never block a commit.

## Audit

Each successful commit writes `import.committed` (category `organisation`,
`actorRole` of the caller) with `newValues`
`{ importRunId, type, fileName, sha256, skipInvalid, totalRows, validRows, invalidRows, inserted, updated, unchanged }`.
Imports may also write per-row entries, listed with each type below.

## Demo files and smoke test

`ml-service/scripts/make_import_files.py` writes import-ready files from
the generator data into `ml-service/data/import_samples/` (git-ignored):
4 departments, 1000 employees, 1000 SIMs, 90000 usage rows and a 100000
row usage file for timing. How departments, employees, carriers, ICCIDs and
phone numbers are assigned is in [ml-service/README.md](../../ml-service/README.md).
All 1000 demo SIMs have 90 consecutive days, so all are scorable.

[imports-smoke.ps1](imports-smoke.ps1) checks this contract against a
running Development API on a throwaway database (the imported rows stay).
Generate the demo files first, then run it with the same credentials setup
as [roles-smoke.ps1](roles-smoke.ps1). It reports the time of each demo
commit; on a laptop with Postgres in Docker the 100000 row usage commit
took about 4 seconds.

