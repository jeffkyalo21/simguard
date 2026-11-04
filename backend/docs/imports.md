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

## Audit

Each successful commit writes `import.committed` (category `organisation`,
`actorRole` of the caller) with `newValues`
`{ importRunId, type, fileName, sha256, skipInvalid, totalRows, validRows, invalidRows, inserted, updated, unchanged }`.
Imports may also write per-row entries, listed with each type below.
